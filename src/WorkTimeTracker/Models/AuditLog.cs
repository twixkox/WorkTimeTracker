namespace WorkTimeTracker.Models;

/// <summary>Журнал аудита: кто, когда и что изменил.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    /// <summary>Действие: Created/Updated/Deleted/Submitted/Approved/Rejected/Imported и т.п.</summary>
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}
