using System.Security.Claims;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>Запись действий пользователей в журнал аудита.</summary>
public class AuditService
{
    private readonly ApplicationDbContext _db;

    public AuditService(ApplicationDbContext db) => _db = db;

    public async Task LogAsync(string userId, string? userName, string action, string entityName, string? entityId = null, string? details = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserName = userName ?? string.Empty,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
        });
        await _db.SaveChangesAsync();
    }

    public static string UserName(ClaimsPrincipal user)
        => user.FindFirst("DisplayName")?.Value
           ?? user.Identity?.Name
           ?? string.Empty;
}
