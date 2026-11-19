using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace WorkTimeTracker.Telegram;

public interface ITelegramBotApi
{
    bool IsEnabled { get; }

    Task SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(string fileId, Stream destination, CancellationToken cancellationToken = default);
}

public sealed class TelegramBotApi : ITelegramBotApi
{
    private readonly ITelegramBotClient? _client;

    public TelegramBotApi(ITelegramBotClient? client = null) => _client = client;

    public bool IsEnabled => _client is not null;

    public async Task SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        await client.SendMessage(chatId, text, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        await client.AnswerCallbackQuery(callbackQueryId, text, showAlert, cancellationToken: cancellationToken);
    }

    public async Task DownloadFileAsync(string fileId, Stream destination,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var file = await client.GetFile(fileId, cancellationToken);
        await client.DownloadFile(file, destination, cancellationToken);
    }

    private ITelegramBotClient GetClient()
        => _client ?? throw new InvalidOperationException("Telegram-бот отключён: не задан токен.");
}
