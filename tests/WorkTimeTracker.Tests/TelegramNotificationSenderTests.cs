using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;
using WorkTimeTracker.Models;
using WorkTimeTracker.Telegram;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TelegramNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_WithoutToken_DoesNotCallTelegram()
    {
        var api = new FakeTelegramBotApi { IsEnabled = true };
        var sender = new TelegramNotificationSender(api, Options.Create(new TelegramOptions()));
        var user = new ApplicationUser { TelegramUserId = 123 };

        await sender.SendAsync(user, "Заголовок", "Сообщение", "/Engineering/Details/1");

        Assert.Empty(api.Messages);
    }

    [Fact]
    public async Task SendAsync_WithoutLinkedTelegram_DoesNotCallTelegram()
    {
        var api = new FakeTelegramBotApi { IsEnabled = true };
        var sender = new TelegramNotificationSender(api, Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            SiteBaseUrl = "https://worktime.example.test",
        }));

        await sender.SendAsync(new ApplicationUser(), "Заголовок", "Сообщение", null);

        Assert.Empty(api.Messages);
    }

    [Fact]
    public async Task SendAsync_WithLinkedTelegram_SendsTextAndAbsoluteSiteLink()
    {
        var api = new FakeTelegramBotApi { IsEnabled = true };
        var sender = new TelegramNotificationSender(api, Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            SiteBaseUrl = "https://worktime.example.test",
        }));

        await sender.SendAsync(new ApplicationUser { TelegramUserId = 123 }, "Новая задача", "Разработать чертёж",
            "/Engineering/Details/42");

        var message = Assert.Single(api.Messages);
        Assert.Equal(123, message.ChatId);
        Assert.Equal("Новая задача\nРазработать чертёж\nhttps://worktime.example.test/Engineering/Details/42",
            message.Text);
    }

    private sealed class FakeTelegramBotApi : ITelegramBotApi
    {
        public bool IsEnabled { get; set; }
        public List<(long ChatId, string Text)> Messages { get; } = new();

        public Task SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((chatId, text));
            return Task.CompletedTask;
        }

        public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DownloadFileAsync(string fileId, Stream destination,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
