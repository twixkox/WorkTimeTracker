namespace WorkTimeTracker.Models;

public enum NotificationCategory
{
    Timesheet,
    Engineering,
    System,
}

/// <summary>Внутрисистемное уведомление пользователя.</summary>
public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Link { get; set; }
    public NotificationCategory Category { get; set; } = NotificationCategory.System;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
