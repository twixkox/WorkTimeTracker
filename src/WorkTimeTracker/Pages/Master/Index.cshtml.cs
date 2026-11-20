using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Master;

public class IndexModel : PageModel
{
    private readonly ReportService _reports;
    private readonly CurrentUserService _currentUser;
    private readonly SavedFilterService _savedFilters;

    public IndexModel(ReportService reports, CurrentUserService currentUser, SavedFilterService savedFilters)
    {
        _reports = reports;
        _currentUser = currentUser;
        _savedFilters = savedFilters;
    }

    public Department? Department { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public DateOnly Today { get; private set; }
    public DateOnly WeekFrom => Today.AddDays(-((7 + (int)Today.DayOfWeek - 1) % 7));
    public DateOnly WeekTo => WeekFrom.AddDays(6);
    public DateOnly MonthFrom => new(Today.Year, Today.Month, 1);
    public DateOnly MonthTo => MonthFrom.AddMonths(1).AddDays(-1);
    public bool FiltersRestored { get; private set; }
    public List<(Models.Employee Employee, List<WorkDay> Days)> Rows { get; private set; } = new();

    public async Task<IActionResult> OnGet(DateOnly? from, DateOnly? to)
    {
        var employee = await _currentUser.GetEmployeeAsync();
        if (employee is null)
        {
            TempData["Error"] = "Учётная запись мастера не привязана к участку.";
            return RedirectToPage("/Index");
        }
        Department = employee.Department;

        var hasQuery = Request.Query.Count > 0;
        var reset = Request.Query.ContainsKey("reset");
        if (!hasQuery)
        {
            var saved = await _savedFilters.GetAsync(SavedFilterService.MasterPage);
            FiltersRestored = saved.Count > 0;
            from = SavedDate(saved, "from");
            to = SavedDate(saved, "to");
        }

        Today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? WeekFrom;
        if (From > Today) From = Today.AddDays(-6);
        To = to ?? From.AddDays(13);

        if (reset)
            await _savedFilters.ResetAsync(SavedFilterService.MasterPage);
        else if (hasQuery)
            await SaveFiltersAsync();

        Rows = await _reports.GetSectionDaysAsync(Department!.Id, From, To);
        return Page();
    }

    private Task SaveFiltersAsync() => _savedFilters.SaveAsync(SavedFilterService.MasterPage,
        new Dictionary<string, string?>
        {
            ["from"] = From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });

    private static DateOnly? SavedDate(IReadOnlyDictionary<string, string> saved, string key)
        => saved.TryGetValue(key, out var value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
}
