using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Production;

/// <summary>Обзор и одноэтапное утверждение табелей всех участков.</summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;

    public IndexModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser,
        NotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public List<Department> Departments { get; private set; } = new();
    public List<WorkDay> Days { get; private set; } = new();

    public async Task<IActionResult> OnGet(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? today.AddDays(-(int)today.DayOfWeek + 1);
        if (From > today) From = today.AddDays(-6);
        To = to ?? From.AddDays(13);

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

    public (int Submitted, int Approved, decimal Hours) Stats(int departmentId)
    {
        var days = Days.Where(d => d.Employee?.DepartmentId == departmentId).ToList();
        return (
            days.Count(d => d.Status == TimeSheetStatus.Submitted),
            days.Count(d => d.Status == TimeSheetStatus.Approved),
            Math.Round(days.Sum(d => d.WorkedHours), 2));
    }

    private IActionResult RedirectToReview(DateOnly? from, DateOnly? to)
        => RedirectToPage(new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") });
}
