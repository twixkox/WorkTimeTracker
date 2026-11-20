using WorkTimeTracker.Models;

namespace WorkTimeTracker.Telegram;

public interface ITelegramNotificationSender
{
    Task SendAsync(ApplicationUser user, string title, string message, string? link,
        CancellationToken cancellationToken = default,
        NotificationCategory category = NotificationCategory.System);
}
