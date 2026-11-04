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

    public IndexModel(ReportService reports, CurrentUserService currentUser)
    {
        _reports = reports;
        _currentUser = currentUser;
    }

    public Department? Department { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
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

        var today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? today.AddDays(-(int)today.DayOfWeek + 1); // начало текущей недели
        if (From > today) From = today.AddDays(-6);
        To = to ?? From.AddDays(13);

        Rows = await _reports.GetSectionDaysAsync(Department!.Id, From, To);
        return Page();
    }
}
