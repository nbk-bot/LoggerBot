namespace LoggerBot;

public static class Configure
{
    /// <summary>
    /// Registers <see cref="ILoggerService"/> as a singleton, binds <see cref="LoggerBotOptions"/>
    /// from the "LoggerBot" configuration section, and registers a named <see cref="System.Net.Http.HttpClient"/>
    /// ("LoggerBot") used by the underlying Telegram bot client.
    /// </summary>
    public static IServiceCollection AddLoggerBot(this IServiceCollection services)
    {
        services.AddOptions<LoggerBotOptions>()
                .Configure<IConfiguration>((opts, cfg) =>
                {
                    cfg.GetSection(LoggerBotOptions.SectionName).Bind(opts);
                });

        services.AddHttpClient("LoggerBot");
        services.AddSingleton<ILoggerService, LoggerService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ILoggerService"/> with explicit options configuration.
    /// </summary>
    public static IServiceCollection AddLoggerBot(this IServiceCollection services, Action<LoggerBotOptions> configure)
    {
        services.AddOptions<LoggerBotOptions>()
                .Configure<IConfiguration>((opts, cfg) =>
                {
                    cfg.GetSection(LoggerBotOptions.SectionName).Bind(opts);
                })
                .Configure(configure);

        services.AddHttpClient("LoggerBot");
        services.AddSingleton<ILoggerService, LoggerService>();
        return services;
    }
}
