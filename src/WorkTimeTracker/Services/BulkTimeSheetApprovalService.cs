using System.Data;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Security;

namespace WorkTimeTracker.Services;

public enum BulkTimeSheetApprovalStatus
{
    Approved,
    AlreadyProcessed,
    NotFound,
    Error,
}

public class BulkTimeSheetApprovalResult
{
    public int DayId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public DateOnly? Date { get; init; }
    public BulkTimeSheetApprovalStatus Status { get; init; }

    public string StatusText => Status switch
    {
        BulkTimeSheetApprovalStatus.Approved => "Утверждён",
        BulkTimeSheetApprovalStatus.AlreadyProcessed => "Уже обработан или не на утверждении",
        BulkTimeSheetApprovalStatus.NotFound => "Не найден",
        _ => "Ошибка",
    };

    public string DisplayText
    {
        get
        {
            var employee = string.IsNullOrWhiteSpace(EmployeeName) ? $"Табель №{DayId}" : EmployeeName;
            return Date is null
                ? $"{employee} — {StatusText}"
                : $"{employee}, {Date:dd.MM.yyyy} — {StatusText}";
        }
    }
}

/// <summary>Массовое утверждение табелей с независимой обработкой каждого рабочего дня.</summary>
public class BulkTimeSheetApprovalService
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;
    private readonly ILogger<BulkTimeSheetApprovalService>? _logger;
    private readonly IAccessService? _access;

    public BulkTimeSheetApprovalService(ApplicationDbContext db, AuditService audit,
        CurrentUserService currentUser, NotificationService notifications,
        ILogger<BulkTimeSheetApprovalService>? logger = null, IAccessService? access = null)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
        _access = access;
    }

    public async Task<IReadOnlyList<BulkTimeSheetApprovalResult>> ApproveAsync(IEnumerable<int> dayIds,
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var results = new List<BulkTimeSheetApprovalResult>();
        foreach (var dayId in dayIds.Distinct())
            results.Add(await ApproveOneAsync(dayId, from, to, cancellationToken));
        return results;
    }

    public async Task<IReadOnlyList<BulkTimeSheetApprovalResult>> ApproveAllAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var dayIds = await _db.WorkDays.AsNoTracking()
            .Where(w => w.Date >= from && w.Date <= to && w.Status == TimeSheetStatus.Submitted)
            .OrderBy(w => w.Date).ThenBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
        return await ApproveAsync(dayIds, from, to, cancellationToken);
    }

    private async Task<BulkTimeSheetApprovalResult> ApproveOneAsync(int dayId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        var employeeName = string.Empty;
        DateOnly? date = null;
        _db.ChangeTracker.Clear();

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                cancellationToken);
            var day = await _db.WorkDays.AsNoTracking()
                .Include(w => w.Employee)
                .FirstOrDefaultAsync(w => w.Id == dayId, cancellationToken);
            if (day is null || day.Date < from || day.Date > to)
                return Result(dayId, BulkTimeSheetApprovalStatus.NotFound);

            employeeName = day.Employee?.FullName ?? string.Empty;
            date = day.Date;
            if (!await CanApproveAsync(day.Employee?.DepartmentId, cancellationToken))
                return Result(dayId, BulkTimeSheetApprovalStatus.Error, employeeName, date);

            _db.WorkDays.Attach(day);
            if (!WorkflowService.TryApprove(day, _currentUser.UserId ?? string.Empty, _currentUser.UserName))
                return Result(dayId, BulkTimeSheetApprovalStatus.AlreadyProcessed, employeeName, date);

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync(_currentUser.UserId ?? string.Empty, _currentUser.UserName, "Approved", "WorkDay",
                day.Id.ToString(), $"{day.Employee?.FullName}, {day.Date:dd.MM.yyyy}");
            if (day.Employee?.UserId is not null)
                await _notifications.NotifyAsync(day.Employee.UserId, "Табель утверждён",
                    $"Ваш табель за {day.Date:dd.MM.yyyy} утверждён.", "/Employee/History");

            await transaction.CommitAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return Result(dayId, BulkTimeSheetApprovalStatus.Approved, employeeName, date);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _db.ChangeTracker.Clear();
            _logger?.LogError(exception, "Не удалось утвердить табель {WorkDayId} при массовой обработке", dayId);
            return Result(dayId, BulkTimeSheetApprovalStatus.Error, employeeName, date);
        }
    }

    private async Task<bool> CanApproveAsync(int? departmentId, CancellationToken cancellationToken)
    {
        if (_access is not null && _currentUser.UserId is { } userId)
            return await _access.HasAccessAsync(userId, AppPermissions.TimeSheetsApprove,
                departmentId: departmentId, cancellationToken: cancellationToken);
        return _currentUser.IsInRole(AppRoles.ProductionHead) || _currentUser.IsInRole(AppRoles.Admin);
    }

    private static BulkTimeSheetApprovalResult Result(int dayId, BulkTimeSheetApprovalStatus status,
        string employeeName = "", DateOnly? date = null) => new()
    {
        DayId = dayId,
        EmployeeName = employeeName,
        Date = date,
        Status = status,
    };
}
