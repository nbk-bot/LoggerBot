using System.Net.Http;
using Microsoft.Extensions.Hosting;

namespace LoggerBot.Services;

public partial class LoggerService : ILoggerService
{
    private readonly TelegramBotClient botClient;
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment hostEnvironment;
    private readonly LoggerBotOptions _options;
    private readonly ILogger<LoggerService>? _logger;

    /// <summary>
    /// Primary constructor. Uses strongly-typed <see cref="LoggerBotOptions"/> and an
    /// <see cref="IHttpClientFactory"/>-managed <see cref="HttpClient"/>.
    /// </summary>
    public LoggerService(
        IOptions<LoggerBotOptions> options,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IHttpClientFactory? httpClientFactory = null,
        ILogger<LoggerService>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.configuration = configuration;
        this.hostEnvironment = hostEnvironment;
        _logger = logger;

        var token = _options.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            // Fallback: try IConfiguration directly for backwards-compatibility
            // with consumers that haven't bound LoggerBotOptions.
            token = configuration["LoggerBot:Token"];
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("LoggerBot:Token is missing in configuration");
        }

        if (httpClientFactory is not null)
        {
            var httpClient = httpClientFactory.CreateClient("LoggerBot");
            botClient = new TelegramBotClient(token!, httpClient);
        }
        else
        {
            botClient = new TelegramBotClient(token!);
        }
    }

    /// <summary>
    /// Legacy constructor preserved for backwards compatibility. Prefer the constructor
    /// that takes <see cref="IOptions{LoggerBotOptions}"/>.
    /// </summary>
    [Obsolete("Bind IOptions<LoggerBotOptions> instead.", error: false)]
    public LoggerService(IConfiguration configuration, IHostEnvironment hostEnvironment)
        : this(
              Microsoft.Extensions.Options.Options.Create(BuildOptionsFromConfiguration(configuration)),
              configuration,
              hostEnvironment,
              httpClientFactory: null,
              logger: null)
    {
    }

    private static LoggerBotOptions BuildOptionsFromConfiguration(IConfiguration configuration)
    {
        var opts = new LoggerBotOptions();
        configuration?.GetSection(LoggerBotOptions.SectionName).Bind(opts);
        return opts;
    }

    public Task ErrorMessageAsync(string message, string? projectName = null, CancellationToken cancellationToken = default)
    {
        Enqueue(message, LogType.Error, projectName, null, cancellationToken);
        return Task.CompletedTask;
    }

    public Task ErrorAttachmentAsync(string message, byte[] fileBytes, string? projectName = null, CancellationToken cancellationToken = default)
    {
        Enqueue(message, LogType.Error, projectName, fileBytes, cancellationToken);
        return Task.CompletedTask;
    }

    public Task ErrorAsync(Exception exception, string? projectName = null, bool detailed = false, CancellationToken cancellationToken = default)
    {
        EnqueueException(exception, projectName, detailed, cancellationToken);
        return Task.CompletedTask;
    }

    public Task InfoAsync(string message, string? projectName = null, CancellationToken cancellationToken = default)
    {
        Enqueue(message, LogType.Info, projectName, null, cancellationToken);
        return Task.CompletedTask;
    }

    public Task SuccessAsync(string message, string? projectName = null, CancellationToken cancellationToken = default)
    {
        Enqueue(message, LogType.Success, projectName, null, cancellationToken);
        return Task.CompletedTask;
    }

    public Task WarningAsync(string message, string? projectName = null, CancellationToken cancellationToken = default)
    {
        Enqueue(message, LogType.Warning, projectName, null, cancellationToken);
        return Task.CompletedTask;
    }

    public Task MessageAsync(string message, string? projectName = null, CancellationToken cancellationToken = default)
    {
        Enqueue(message, LogType.Message, projectName, null, cancellationToken);
        return Task.CompletedTask;
    }

    private void Enqueue(string text, LogType logType, string? projectName, byte[]? fileBytes, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        text = logType switch
        {
            LogType.Error => $"*[❌ERROR]* `{now}`\n\n{text}",
            LogType.Info => $"*[ℹ️INFO]* `{now}`\n\n{text}",
            LogType.Warning => $"*[⚠️WARNING]* `{now}`\n\n{text}",
            LogType.Success => $"*[✅SUCCESS]* `{now}`\n\n{text}",
            LogType.Message => $"*[📩MESSAGE]* `{now}`\n\n{text}",
            _ => $"*[📩MESSAGE]* `{now}`:\n\n{text}"
        };

        if (fileBytes is not null)
        {
            Add(new(GetChatId(projectName), text, true, fileBytes, cancellationToken));
            return;
        }

        Add(new(GetChatId(projectName), text, false, null, cancellationToken));
    }

    private void EnqueueException(Exception exception, string? projectName, bool detailed, CancellationToken cancellationToken)
    {
        if (detailed)
        {
            var content = GetFullExceptionDetails(exception);
            var bytes = Encoding.UTF8.GetBytes(content);
            var text = $@"*[❌ERROR]* `{DateTime.Now}`

                {exception.Message}";

            Add(new(GetChatId(projectName), text, true, bytes, cancellationToken));
            return;
        }

        var stackTrace = new StackTrace(exception, true);
        string sourceLines = "";

        // Include up to 10 lines of source information if available
        var frames = stackTrace.GetFrames();
        if (frames != null)
        {
            for (int i = 0; i < Math.Min(10, frames.Length); i++)
            {
                var frame = frames[i];
                var fileName = frame.GetFileName();
                var lineNumber = frame.GetFileLineNumber();
                var methodName = frame.GetMethod()?.Name;

                if (!string.IsNullOrWhiteSpace(fileName) && lineNumber > 0)
                {
                    sourceLines += $"\n    At {fileName}:{lineNumber} in {methodName}";
                }
            }
        }

        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine($"*[❌ERROR]* `{DateTime.Now}`");
        var env = hostEnvironment.EnvironmentName;
        if (!string.IsNullOrEmpty(env))
        {
            stringBuilder.AppendLine($"Environment: {env}");
        }
        stringBuilder.AppendLine();
        stringBuilder.AppendLine($"\U0001f6d1{exception.GetType().Name}: {exception.Message}");
        if (exception.InnerException is not null)
        {
            stringBuilder.AppendLine($"Inner Exception: {exception.InnerException?.Message}");
        }
        stringBuilder.AppendLine();
        stringBuilder.AppendLine($"\U0001fab2Source: {sourceLines}");

        Add(new(GetChatId(projectName), stringBuilder.ToString(), false, null, cancellationToken));
    }

    public static string GetFullExceptionDetails(Exception ex)
    {
        if (ex == null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("🔥 Exception Details:");

        CollectExceptionDetails(ex, sb, 0);

        return sb.ToString();
    }

    private static void CollectExceptionDetails(Exception ex, StringBuilder sb, int level)
    {
        if (ex == null) return;

        string indent = new string(' ', level * 4); // Indent inner exceptions
        sb.AppendLine($"{indent}📌 Message: {ex.Message}");
        sb.AppendLine($"{indent}🔍 Type: {ex.GetType().FullName}");
        sb.AppendLine($"{indent}📍 StackTrace: {ex.StackTrace}");

        // Handle AggregateException separately (for Task and Parallel exceptions)
        if (ex is AggregateException aggEx)
        {
            foreach (var inner in aggEx.InnerExceptions)
            {
                sb.AppendLine($"{indent}🔄 Aggregate Inner Exception:");
                CollectExceptionDetails(inner, sb, level + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine($"{indent}➡ Inner Exception:");
            CollectExceptionDetails(ex.InnerException, sb, level + 1);
        }
    }

    private long GetChatId(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            // Prefer strongly-typed options when project not specified.
            if (_options.ChatId != 0)
            {
                return _options.ChatId;
            }

            projectName = "ChatId";
        }

        if (long.TryParse(configuration[$"LoggerBot:{projectName}"], out long result))
        {
            return result;
        }
        else
        {
            throw new InvalidOperationException("ChatId not found!");
        }
    }
}
