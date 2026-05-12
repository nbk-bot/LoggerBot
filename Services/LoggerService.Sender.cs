using Telegram.Bot.Exceptions;

namespace LoggerBot.Services;

public partial class LoggerService
{
    private readonly ConcurrentQueue<LogMessage> _messageQueue = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // 0 = idle, 1 = a worker is running. Interlocked.CompareExchange ensures
    // that at most one ProcessQueueAsync loop is active at any time.
    private int _isProcessing = 0;

    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(35); // 30 msg/sec → ~35ms delay
    private static readonly TimeSpan GroupLimitDelay = TimeSpan.FromSeconds(3); // 20 msg/min → 3 sec delay
    private readonly Dictionary<long, DateTime> _groupLastSentTime = new();

    private void Add(LogMessage message)
    {
        _messageQueue.Enqueue(message);
        StartWorker();
    }

    private void StartWorker()
    {
        if (_messageQueue.IsEmpty)
        {
            return;
        }

        // Try to flip _isProcessing from 0 -> 1. Only the thread that wins this
        // race actually spawns the worker task.
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
        {
            Task.Run(ProcessQueueAsync);
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (_messageQueue.TryDequeue(out var message))
            {
                var delay = GetGroupDelay(message.ChatId);
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, message.CancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Caller asked us to stop sending this particular message.
                        continue;
                    }
                }

                await SendMessageWithRetry(message).ConfigureAwait(false);
                _groupLastSentTime[message.ChatId] = DateTime.UtcNow;

                await Task.Delay(RateLimitDelay).ConfigureAwait(false);
            }
        }
        finally
        {
            // Release the worker slot.
            Interlocked.Exchange(ref _isProcessing, 0);

            // A message may have been enqueued after our last TryDequeue but
            // before we released the slot; re-arm the worker if so.
            if (!_messageQueue.IsEmpty)
            {
                StartWorker();
            }
        }
    }

    private async Task SendMessageWithRetry(LogMessage message)
    {
        int retryCount = 0;

        while (retryCount < 5)
        {
            try
            {
                await SendMessage(message).ConfigureAwait(false);
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                int retryAfter = ex.Parameters?.RetryAfter ?? 5;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryAfter), message.CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                retryCount++;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send message to chat {ChatId}", message.ChatId);
                return;
            }
        }

        _logger?.LogError("Max retries reached. Dropping message for chat {ChatId}", message.ChatId);
    }

    private async Task SendMessage(LogMessage message)
    {
        if (message.HasDocument)
        {
            using var stream = new MemoryStream(message.FileBytes!);
            await botClient.SendDocumentAsync(
                chatId: message.ChatId,
                document: new InputFileStream(stream, "details.json"),
                caption: message.Text,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                disableNotification: true,
                cancellationToken: message.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            await botClient.SendTextMessageAsync(
                chatId: message.ChatId,
                text: message.Text,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                disableNotification: true,
                cancellationToken: message.CancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan GetGroupDelay(long chatId)
    {
        if (_groupLastSentTime.TryGetValue(chatId, out var lastSent))
        {
            var elapsed = DateTime.UtcNow - lastSent;
            return elapsed < GroupLimitDelay ? GroupLimitDelay - elapsed : TimeSpan.Zero;
        }

        return TimeSpan.Zero;
    }
}
