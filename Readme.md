# LoggerBot

Telegram bot orqali ASP.NET Core ilovalardan log yuborish uchun NuGet paket. v1.6.0 — `IOptions<LoggerBotOptions>`, `IHttpClientFactory`, va multi-target net6-10.

## Quick install

```bash
dotnet add package LoggerBot --version 1.6.0
```

## Targets

`net6.0` / `net7.0` / `net8.0` / `net9.0` / `net10.0`

## Configuration

`appsettings.json` ichida `LoggerBot` sectionini to'ldiring:

```json
{
  "LoggerBot": {
    "Token": "123456:bot-token",
    "ChatId": -100123456789
  }
}
```

Section nomi `LoggerBotOptions.SectionName` ("LoggerBot") orqali default keladi; options overload bilan o'zingiz qayta belgilashingiz mumkin.

## Registration (recommended — `IOptions<LoggerBotOptions>`)

```csharp
// appsettings.json'dan bind:
builder.Services.AddLoggerBot();

// yoki dasturiy ravishda:
builder.Services.AddLoggerBot(o =>
{
    o.Token = "123456:bot-token";
    o.ChatId = -100123456789;
});
```

Ikkala overload ham `LoggerBotOptions`, named `HttpClient` ("LoggerBot") va `ILoggerService` singletonni ro'yxatga oladi.

## Usage

`ILoggerService` ni controller yoki handlerga inject qiling. Har bir metod `CancellationToken` qabul qiladi va u `botClient.SendTextMessageAsync` ga uzatiladi.

```csharp
using LoggerBot.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ILoggerService _logger;

    public OrdersController(ILoggerService logger) => _logger = logger;

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await _logger.InfoAsync("Order create boshlandi", cancellationToken: ct);

        try
        {
            // ... biznes logikasi
            await _logger.SuccessAsync("Order yaratildi", cancellationToken: ct);
            await _logger.MessageAsync("Audit: yangi buyurtma", cancellationToken: ct);
            await _logger.WarningAsync("Stok kam qoldi", cancellationToken: ct);
            return Ok();
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync(ex, detailed: true, cancellationToken: ct);

            var dump = System.Text.Encoding.UTF8.GetBytes(ex.ToString());
            await _logger.ErrorAttachmentAsync("Order yaratishda xatolik", dump, cancellationToken: ct);
            return StatusCode(500);
        }
    }
}
```

## Legacy API (deprecated)

Eski `LoggerService(IConfiguration, IHostEnvironment)` konstruktori `[Obsolete]` deb belgilangan. Yangi konstruktor `IOptions<LoggerBotOptions>` qabul qiladi — DI orqali avtomatik chaqiriladi. Iltimos `IOptions<LoggerBotOptions>` ga ko'chib o'ting.

## What's new in 1.6.0

- Multi-target: `net6.0` / `net7.0` / `net8.0` / `net9.0` / `net10.0`
- Strongly-typed `LoggerBotOptions` (`Token`, `ChatId`, `SectionName`)
- `IHttpClientFactory` orqali named `HttpClient` ("LoggerBot") — `TelegramBotClient` shu HttpClient ustida ishlaydi
- `Interlocked.CompareExchange` bilan race-free queue worker — aniq bitta worker ishga tushadi
- `CancellationToken` butun yo'l bo'ylab `botClient.SendTextMessageAsync` ga uzatiladi
- `Console.WriteLine` olib tashlandi — agar inject qilingan bo'lsa `ILogger<LoggerService>` ishlatiladi

## Multi-project chats

Bir nechta loyihaga (chatga) yozish kerak bo'lsa, `appsettings.json` ga qo'shimcha kalitlar qo'shing va metod chaqirig'ida `projectName` bering:

```json
{
  "LoggerBot": {
    "Token": "bot-token",
    "ChatId": -100default,
    "Project1": -100chatId1,
    "Project2": -100chatId2
  }
}
```

```csharp
await _logger.ErrorAsync(exception, "Project1", detailed: true, cancellationToken: ct);
```

## License / contributing

Issue va PR'lar ochiq — `https://github.com/nbk-bot/LoggerBot`. Litsenziya repo ildizidagi `LICENSE` fayliga muvofiq.
