using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Master;

public class SummaryModel : PageModel
{
    private readonly ReportService _reports;
    private readonly ExcelService _excel;
    private readonly CurrentUserService _currentUser;

    public SummaryModel(ReportService reports, ExcelService excel, CurrentUserService currentUser)
    {
        _reports = reports;
        _excel = excel;
        _currentUser = currentUser;
    }

    public Department? Department { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public List<BoatSummaryRow> Boats { get; private set; } = new();
    public List<EmployeeLoadRow> Load { get; private set; } = new();
    public (decimal Hours, int Count) Inhouse { get; private set; }

    public async Task<IActionResult> OnGet(DateOnly? from, DateOnly? to)
    {
        var me = await _currentUser.GetEmployeeAsync();
        if (me?.Department is null) return RedirectToPage("/Index");
        Department = me.Department;

        var today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? today.AddDays(-(int)today.DayOfWeek + 1);
        if (From > today) From = today.AddDays(-6);
        To = to ?? From.AddDays(13);

        Boats = await _reports.GetBoatSummaryAsync(From, To, Department.Id);
        Load = await _reports.GetEmployeeLoadAsync(From, To, Department.Id);
        Inhouse = await _reports.GetInhouseSummaryAsync(From, To, Department.Id);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(DateOnly from, DateOnly to)
    {
        var me = await _currentUser.GetEmployeeAsync();
        if (me?.Department is null) return Forbid();

        var rows = await _reports.GetSectionDaysAsync(me.Department.Id, from, to);
        var boats = await _reports.GetBoatSummaryAsync(from, to, me.Department.Id);
        var bytes = _excel.ExportSectionTimesheet(me.Department, from, to, rows, boats);
        var name = $"Табель_{me.Department.Name}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }
}
