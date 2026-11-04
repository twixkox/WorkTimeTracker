using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>Внутрисистемные уведомления.</summary>
public class NotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db) => _db = db;

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
