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
    private readonly NotificationPreferenceService? _preferences;

    public NotificationService(ApplicationDbContext db, ITelegramNotificationSender? telegramSender = null,
        ILogger<NotificationService>? logger = null, NotificationPreferenceService? preferences = null)
    {
        _db = db;
        _telegramSender = telegramSender;
        _logger = logger;
        _preferences = preferences;
    }

    public async Task NotifyAsync(string userId, string title, string message, string? link = null,
        NotificationCategory? category = null)
    {
        var resolvedCategory = category ?? InferCategory(title, link);
        var preferences = _preferences is null
            ? new NotificationPreferences()
            : await _preferences.GetAsync(userId);

        if (preferences.IsInAppEnabled(resolvedCategory))
        {
            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Link = link,
                Category = resolvedCategory,
            });
            await _db.SaveChangesAsync();
        }

        if (_telegramSender is null) return;

        try
        {
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user?.TelegramUserId is null) return;

            await _telegramSender.SendAsync(user, title, message, link, category: resolvedCategory);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Не удалось отправить Telegram-уведомление пользователю {UserId}", userId);
        }
    }

    public async Task<IReadOnlyList<Notification>> GetForUserAsync(string userId, int take = 20,
        bool unreadOnly = false, NotificationCategory? category = null, int skip = 0)
    {
        var query = ForUser(userId, unreadOnly, category);
        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(0, take))
            .ToListAsync();
    }

    public async Task<int> GetCountAsync(string userId, bool unreadOnly = false,
        NotificationCategory? category = null)
        => await ForUser(userId, unreadOnly, category).CountAsync();

    public async Task<int> GetUnreadCountAsync(string userId)
        => await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task MarkReadAsync(string userId, int notificationId)
    {
        await _db.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }

    public async Task MarkAllReadAsync(string userId)
    {
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }

    private IQueryable<Notification> ForUser(string userId, bool unreadOnly, NotificationCategory? category)
    {
        var query = _db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly)
            query = query.Where(n => !n.IsRead);
        if (category is not null)
            query = query.Where(n => n.Category == category);
        return query;
    }

    private static NotificationCategory InferCategory(string title, string? link)
    {
        if (link?.StartsWith("/Engineering", StringComparison.OrdinalIgnoreCase) == true
            || title.Contains("КБ", StringComparison.OrdinalIgnoreCase)
            || title.Contains("задач", StringComparison.OrdinalIgnoreCase))
            return NotificationCategory.Engineering;

        if (link?.StartsWith("/Employee", StringComparison.OrdinalIgnoreCase) == true
            || link?.StartsWith("/Master", StringComparison.OrdinalIgnoreCase) == true
            || link?.StartsWith("/Production", StringComparison.OrdinalIgnoreCase) == true
            || title.Contains("табел", StringComparison.OrdinalIgnoreCase))
            return NotificationCategory.Timesheet;

        return NotificationCategory.System;
    }
}
