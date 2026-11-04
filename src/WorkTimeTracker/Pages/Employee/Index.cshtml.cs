using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Employee;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;

    public IndexModel(ApplicationDbContext db, CurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public Models.Employee? Employee { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public List<WorkDay> Days { get; private set; } = new();
    public decimal MonthHours { get; private set; }

    /// <summary>День недели начала месяца (0 = пн) и количество дней.</summary>
    public int FirstDayOffset { get; private set; }
    public int DaysInMonth { get; private set; }

    public async Task<IActionResult> OnGet(int? year, int? month)
    {
        Employee = await _currentUser.GetEmployeeAsync();
        if (Employee is null)
        {
            TempData["Error"] = "Ваша учётная запись не привязана к карточке сотрудника. Обратитесь к администратору.";
            return Page();
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        Year = year ?? today.Year;
        Month = month ?? today.Month;
        if (Month < 1 || Month > 12) Month = today.Month;

        var from = new DateOnly(Year, Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        Days = await _db.WorkDays
            .Include(w => w.TimeEntries)
            .Where(w => w.EmployeeId == Employee.Id && w.Date >= from && w.Date <= to)
            .ToListAsync();
        MonthHours = Math.Round(Days.Sum(d => d.WorkedHours), 2);

        FirstDayOffset = ((int)from.DayOfWeek + 6) % 7; // понедельник = 0
        DaysInMonth = to.Day;
        return Page();
    }

    public WorkDay? DayAt(int day) => Days.FirstOrDefault(d => d.Date.Day == day);

    public string DayUrl(Models.Employee employee, DateOnly date) =>
        employee.Department?.IsWeeklyView == true
            ? $"/Employee/Week?week={date:yyyy-MM-dd}"
            : $"/Employee/Day?date={date:yyyy-MM-dd}";
}
