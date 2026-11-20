using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Security;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Production;

/// <summary>Обзор и одноэтапное утверждение табелей всех участков.</summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;
    private readonly BulkTimeSheetApprovalService _bulkApproval;
    private readonly SavedFilterService? _savedFilters;
    private readonly IAccessService? _access;

    public IndexModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser,
        NotificationService notifications)
        : this(db, audit, currentUser, notifications,
            new BulkTimeSheetApprovalService(db, audit, currentUser, notifications))
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public IndexModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser,
        NotificationService notifications, BulkTimeSheetApprovalService bulkApproval,
        SavedFilterService? savedFilters = null, IAccessService? access = null)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _notifications = notifications;
        _bulkApproval = bulkApproval;
        _savedFilters = savedFilters;
        _access = access;
    }

    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public DateOnly Today { get; private set; }
    public DateOnly WeekFrom => Today.AddDays(-((7 + (int)Today.DayOfWeek - 1) % 7));
    public DateOnly WeekTo => WeekFrom.AddDays(6);
    public DateOnly MonthFrom => new(Today.Year, Today.Month, 1);
    public DateOnly MonthTo => MonthFrom.AddMonths(1).AddDays(-1);
    public bool FiltersRestored { get; private set; }
    public List<Department> Departments { get; private set; } = new();
    public List<WorkDay> Days { get; private set; } = new();

    public async Task<IActionResult> OnGet(DateOnly? from, DateOnly? to)
    {
        var hasQuery = Request.Query.Count > 0;
        var reset = Request.Query.ContainsKey("reset");
        if (!hasQuery && _savedFilters is not null)
        {
            var saved = await _savedFilters.GetAsync(SavedFilterService.ProductionPage);
            FiltersRestored = saved.Count > 0;
            from = SavedDate(saved, "from");
            to = SavedDate(saved, "to");
        }
        Today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? WeekFrom;
        if (From > Today) From = Today.AddDays(-6);
        To = to ?? From.AddDays(13);
        if (_savedFilters is not null)
        {
            if (reset) await _savedFilters.ResetAsync(SavedFilterService.ProductionPage);
            else if (hasQuery) await SaveFiltersAsync();
        }

        Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();
        Days = await _db.WorkDays
            .Include(w => w.Employee).ThenInclude(e => e.Department)
            .Where(w => w.Date >= From && w.Date <= To)
            .ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int dayId, DateOnly? from, DateOnly? to)
    {
        var day = await _db.WorkDays.Include(w => w.Employee).FirstOrDefaultAsync(w => w.Id == dayId);
        if (day is null) return NotFound();
        if (!await CanApproveAsync(day.Employee?.DepartmentId)) return Forbid();
        if (!WorkflowService.TryApprove(day, _currentUser.UserId ?? "", _currentUser.UserName))
        {
            TempData["Error"] = "Утвердить можно только табель со статусом «На утверждении».";
            return RedirectToReview(from, to);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Approved", "WorkDay", day.Id.ToString(),
            $"{day.Employee?.FullName}, {day.Date:dd.MM.yyyy}");
        if (day.Employee?.UserId is not null)
            await _notifications.NotifyAsync(day.Employee.UserId, "Табель утверждён",
                $"Ваш табель за {day.Date:dd.MM.yyyy} утверждён.", "/Employee/History");
        TempData["Success"] = "Табель утверждён.";
        return RedirectToReview(from, to);
    }

    public async Task<IActionResult> OnPostBulkApproveAsync(int[]? dayIds, DateOnly from, DateOnly to)
    {
        if (dayIds is null || dayIds.Length == 0)
        {
            TempData["Error"] = "Выберите хотя бы один табель.";
            return RedirectToReview(from, to);
        }

        var results = await _bulkApproval.ApproveAsync(dayIds, from, to);
        SetBulkApprovalResults(results);
        return RedirectToReview(from, to);
    }

    public async Task<IActionResult> OnPostBulkApproveAllAsync(DateOnly from, DateOnly to)
    {
        var results = await _bulkApproval.ApproveAllAsync(from, to);
        if (results.Count == 0)
        {
            TempData["Error"] = "В выбранном диапазоне нет табелей на утверждении.";
            return RedirectToReview(from, to);
        }

        SetBulkApprovalResults(results);
        return RedirectToReview(from, to);
    }

    public (int Submitted, int Approved, decimal Hours) Stats(int departmentId)
    {
        var days = Days.Where(d => d.Employee?.DepartmentId == departmentId).ToList();
        return (
            days.Count(d => d.Status == TimeSheetStatus.Submitted),
            days.Count(d => d.Status == TimeSheetStatus.Approved),
            Math.Round(days.Sum(d => d.WorkedHours), 2));
    }

    private void SetBulkApprovalResults(IReadOnlyList<BulkTimeSheetApprovalResult> results)
    {
        var approved = results.Count(r => r.Status == BulkTimeSheetApprovalStatus.Approved);
        TempData["BulkApprovalSummary"] = $"Обработано: {results.Count}. Утверждено: {approved}.";
        TempData["BulkApprovalResults"] = string.Join("\n", results.Select(r => r.DisplayText));
    }

    private IActionResult RedirectToReview(DateOnly? from, DateOnly? to)
        => RedirectToPage(new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") });

    private async Task<bool> CanApproveAsync(int? departmentId)
    {
        if (_access is not null && _currentUser.UserId is { } userId)
            return await _access.HasAccessAsync(userId, AppPermissions.TimeSheetsApprove, departmentId: departmentId);
        return _currentUser.IsInRole(AppRoles.ProductionHead) || _currentUser.IsInRole(AppRoles.Admin);
    }

    private Task SaveFiltersAsync() => _savedFilters!.SaveAsync(SavedFilterService.ProductionPage,
        new Dictionary<string, string?>
        {
            ["from"] = From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });

    private static DateOnly? SavedDate(IReadOnlyDictionary<string, string> saved, string key)
        => saved.TryGetValue(key, out var value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date : null;
}
