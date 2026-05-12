namespace LoggerBot;

/// <summary>
/// Strongly-typed options for LoggerBot, bound from the "LoggerBot" configuration section.
/// </summary>
public class LoggerBotOptions
{
    public const string SectionName = "LoggerBot";

    /// <summary>
    /// Telegram bot token. Required.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Default chat id to send messages to.
    /// </summary>
    public long ChatId { get; set; }
}
