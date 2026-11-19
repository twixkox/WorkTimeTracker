using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WorkTimeTracker.Telegram;

public sealed class TelegramBotWorker : BackgroundService, IUpdateHandler
{
    private readonly ITelegramBotClient? _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramBotWorker> _logger;

    public TelegramBotWorker(IServiceScopeFactory scopeFactory, IOptions<TelegramOptions> options,
        ILogger<TelegramBotWorker> logger, ITelegramBotClient? client = null)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled || _client is null)
        {
            _logger.LogInformation("Telegram long polling отключён: токен не задан");
            return;
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
        };
        _logger.LogInformation("Запущен Telegram long polling");

        try
        {
            await _client.ReceiveAsync(this, receiverOptions, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Telegram long polling остановлен с ошибкой");
        }
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>();
            await handler.HandleAsync(update, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось обработать Telegram update {UpdateId}", update.Id);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        _logger.LogError(exception, "Ошибка Telegram long polling: {Source}", source);
        return Task.CompletedTask;
    }
}
