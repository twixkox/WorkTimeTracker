using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Employee;

/// <summary>Недельный вид (как лист «15»): 7 блоков «Учёт затрат» по дням недели.</summary>
public class WeekModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly SettingsService _settings;

    public WeekModel(ApplicationDbContext db, CurrentUserService currentUser, SettingsService settings)
    {
        _db = db;
        _currentUser = currentUser;
        _settings = settings;
    }

    public Models.Employee? Employee { get; private set; }
    public DateOnly WeekStart { get; private set; }
    public List<DayFormModel> Days { get; private set; } = new();
    public List<Boat> Boats { get; private set; } = new();
    public List<WorkType> WorkTypes { get; private set; } = new();
    public Dictionary<DateOnly, WorkDay> Existing { get; } = new();

    public async Task<IActionResult> OnGet(DateOnly? week)
    {
        Employee = await _currentUser.GetEmployeeAsync();
        if (Employee is null) return RedirectToPage("/Index");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var any = week ?? today;
        WeekStart = any.AddDays(-((int)any.DayOfWeek + 6) % 7);

        var shift = await _settings.GetShiftTemplateAsync();
        var from = WeekStart;
        var to = WeekStart.AddDays(6);

        var existing = await _db.WorkDays
            .Include(w => w.TimeEntries)
            .Where(w => w.EmployeeId == Employee.Id && w.Date >= from && w.Date <= to)
            .ToListAsync();
        foreach (var d in existing) Existing[d.Date] = d;

        for (var i = 0; i < 7; i++)
        {
            var date = WeekStart.AddDays(i);
            var day = existing.FirstOrDefault(d => d.Date == date);
            Days.Add(new DayFormModel
            {
                Date = date,
                StartTime = day?.StartTime ?? shift?.Start ?? new TimeOnly(8, 0),
                EndTime = day?.EndTime ?? shift?.End ?? new TimeOnly(16, 30),
                ReturnUrl = $"/Employee/Week?week={WeekStart:yyyy-MM-dd}",
                Entries = day?.TimeEntries.Select(e => new EntryInput
                {
                    BoatId = e.BoatId,
                    Hours = e.Hours,
                    WorkDescription = e.WorkDescription,
                    IsInhouse = e.IsInhouse,
                }).ToList() ?? new List<EntryInput>(),
            });
        }

        Boats = await _db.Boats.Where(b => b.IsActive).OrderBy(b => b.Number).ToListAsync();
        WorkTypes = await _db.WorkTypes.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        return Page();
    }

    public bool CanEdit(DateOnly date) =>
        !Existing.TryGetValue(date, out var day) || WorkflowService.CanEmployeeEdit(day.Status);
}
