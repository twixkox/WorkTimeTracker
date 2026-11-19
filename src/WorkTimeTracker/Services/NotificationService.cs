using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Telegram;

namespace WorkTimeTracker.Services;

/// <summary>Внутрисистемные и Telegram-уведомления.</summary>
public class NotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly ITelegramNotificationSender? _telegramSender;
    private readonly ILogger<NotificationService>? _logger;

    public NotificationService(ApplicationDbContext db, ITelegramNotificationSender? telegramSender = null,
        ILogger<NotificationService>? logger = null)
    {
        _db = db;
        _telegramSender = telegramSender;
        _logger = logger;
    }

    public async Task NotifyAsync(string userId, string title, string message, string? link = null)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Link = link,
        });
        await _db.SaveChangesAsync();

        if (_telegramSender is null) return;

        try
        {
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user?.TelegramUserId is null) return;

            await _telegramSender.SendAsync(user, title, message, link);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Не удалось отправить Telegram-уведомление пользователю {UserId}", userId);
        }
    }

    public async Task<IReadOnlyList<Notification>> GetForUserAsync(string userId, int take = 20)
        => await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();

    public async Task<int> GetUnreadCountAsync(string userId)
        => await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task MarkAllReadAsync(string userId)
    {
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }
}
