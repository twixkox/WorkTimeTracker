using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using WorkTimeTracker.Telegram;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TelegramNotificationSenderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly NotificationPreferenceService _preferences;

    public TelegramNotificationSenderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _preferences = new NotificationPreferenceService(_db);
    }

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

    [Fact]
    public async Task SendAsync_WhenTelegramDisabled_DoesNotCallTelegram()
    {
        var api = new FakeTelegramBotApi { IsEnabled = true };
        var sender = CreateSender(api);
        var user = new ApplicationUser { Id = "user", TelegramUserId = 123 };
        await _preferences.SaveAsync(user.Id, new NotificationPreferences { TelegramEnabled = false });

        await sender.SendAsync(user, "Заголовок", "Сообщение", null);

        Assert.Empty(api.Messages);
    }

    [Fact]
    public async Task SendAsync_RespectsTelegramCategoryPreference()
    {
        var api = new FakeTelegramBotApi { IsEnabled = true };
        var sender = CreateSender(api);
        var user = new ApplicationUser { Id = "user", TelegramUserId = 123 };
        await _preferences.SaveAsync(user.Id, new NotificationPreferences { TelegramEngineering = false });

        await sender.SendAsync(user, "Задача", "Сообщение", null, category: NotificationCategory.Engineering);
        await sender.SendAsync(user, "Система", "Сообщение", null, category: NotificationCategory.System);

        Assert.Equal("Система\nСообщение", Assert.Single(api.Messages).Text);
    }

    [Fact]
    public async Task NotifyAsync_DuringQuietHoursSavesInAppButDoesNotCallTelegram()
    {
        var api = new FakeTelegramBotApi { IsEnabled = true };
        var sender = CreateSender(api, new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.FromHours(3)));
        var user = new ApplicationUser
        {
            Id = "user",
            UserName = "user@example.test",
            TelegramUserId = 123,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _preferences.SaveAsync(user.Id, new NotificationPreferences
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(8, 0),
        });
        var notifications = new NotificationService(_db, sender, preferences: _preferences);

        await notifications.NotifyAsync(user.Id, "Новая задача КБ", "Сообщение", "/Engineering/Details/1");

        Assert.Equal(1, await _db.Notifications.CountAsync());
        Assert.Empty(api.Messages);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private TelegramNotificationSender CreateSender(FakeTelegramBotApi api, DateTimeOffset? localNow = null)
        => new(api, Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            SiteBaseUrl = "https://worktime.example.test",
        }), _preferences, localNow is null ? null : new FixedTimeProvider(localNow.Value));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProvider(DateTimeOffset localNow)
        {
            _utcNow = localNow.ToUniversalTime();
            _localTimeZone = TimeZoneInfo.CreateCustomTimeZone("Test", localNow.Offset, "Test", "Test");
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => _localTimeZone;
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
