using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Employee;

public class HistoryModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;

    public HistoryModel(ApplicationDbContext db, CurrentUserService currentUser, NotificationService notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public Models.Employee? Employee { get; private set; }
    public List<WorkDay> Days { get; private set; } = new();
    public List<Notification> Notifications { get; private set; } = new();
    public int Year { get; private set; }
    public int Month { get; private set; }

    public async Task<IActionResult> OnGet(int? year, int? month)
    {
        Employee = await _currentUser.GetEmployeeAsync();
        if (Employee is null) return RedirectToPage("/Index");

        var today = DateOnly.FromDateTime(DateTime.Today);
        Year = year ?? today.Year;
        Month = month ?? today.Month;

        var from = new DateOnly(Year, Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        Days = await _db.WorkDays
            .Include(w => w.TimeEntries).ThenInclude(t => t.Boat)
            .Where(w => w.EmployeeId == Employee.Id && w.Date >= from && w.Date <= to)
            .OrderByDescending(w => w.Date)
            .ToListAsync();

        if (_currentUser.UserId is not null)
        {
            Notifications = (await _notifications.GetForUserAsync(_currentUser.UserId)).ToList();
            await _notifications.MarkAllReadAsync(_currentUser.UserId);
        }
        return Page();
    }
}
