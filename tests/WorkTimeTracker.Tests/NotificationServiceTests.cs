using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using WorkTimeTracker.Telegram;
using Xunit;

namespace WorkTimeTracker.Tests;

public class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public NotificationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task NotifyAsync_SavesInternalNotificationAndSendsTelegramToLinkedUser()
    {
        var user = await AddUserAsync(telegramUserId: 123456789);
        var sender = new FakeTelegramNotificationSender();
        var service = new NotificationService(_db, sender);

        await service.NotifyAsync(user.Id, "Новая задача КБ", "Проверьте задание", "/Engineering/Details/1");

        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(user.Id, notification.UserId);
        Assert.Equal("Новая задача КБ", notification.Title);
        Assert.Equal("Проверьте задание", notification.Message);
        Assert.Equal("/Engineering/Details/1", notification.Link);
        var call = Assert.Single(sender.Calls);
        Assert.Same(user, call.User);
        Assert.Equal(notification.Title, call.Title);
        Assert.Equal(notification.Message, call.Message);
        Assert.Equal(notification.Link, call.Link);
    }

    [Fact]
    public async Task NotifyAsync_DoesNotSendTelegramWithoutTelegramUserId()
    {
        var user = await AddUserAsync();
        var sender = new FakeTelegramNotificationSender();
        var service = new NotificationService(_db, sender);

        await service.NotifyAsync(user.Id, "Уведомление", "Текст");

        Assert.Equal(1, await _db.Notifications.CountAsync());
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task NotifyAsync_WithoutTelegramSenderStillSavesInternalNotification()
    {
        var user = await AddUserAsync(telegramUserId: 123456789);
        var service = new NotificationService(_db);

        await service.NotifyAsync(user.Id, "Уведомление", "Текст");

        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(user.Id, notification.UserId);
    }

    [Fact]
    public async Task NotifyAsync_TelegramFailureIsLoggedAndDoesNotRollbackInternalNotification()
    {
        var user = await AddUserAsync(telegramUserId: 123456789);
        var sender = new FakeTelegramNotificationSender { Error = new InvalidOperationException("Telegram недоступен") };
        var logger = new TestLogger();
        var service = new NotificationService(_db, sender, logger);

        await service.NotifyAsync(user.Id, "Уведомление", "Текст");

        Assert.Equal(1, await _db.Notifications.CountAsync());
        Assert.Single(sender.Calls);
        Assert.Same(sender.Error, Assert.Single(logger.Errors));
    }

    private async Task<ApplicationUser> AddUserAsync(long? telegramUserId = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "engineer@example.test",
            DisplayName = "Инженер",
            TelegramUserId = telegramUserId,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeTelegramNotificationSender : ITelegramNotificationSender
    {
        public List<(ApplicationUser User, string Title, string Message, string? Link)> Calls { get; } = [];
        public Exception? Error { get; init; }

        public Task SendAsync(ApplicationUser user, string title, string message, string? link,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((user, title, message, link));
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }
    }

    private sealed class TestLogger : ILogger<NotificationService>
    {
        public List<Exception> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null)
                Errors.Add(exception);
        }
    }
}
