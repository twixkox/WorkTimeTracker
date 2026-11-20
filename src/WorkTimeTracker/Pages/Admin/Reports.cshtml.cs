using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class ReportsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reports;
    private readonly ExcelService _excel;
    private readonly SavedFilterService _savedFilters;

    public ReportsModel(ApplicationDbContext db, ReportService reports, ExcelService excel, SavedFilterService savedFilters)
    {
        _db = db;
        _reports = reports;
        _excel = excel;
        _savedFilters = savedFilters;
    }

    public SelectList Employees { get; private set; } = default!;
    public SelectList Departments { get; private set; } = default!;
    public int? EmployeeId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public int? DepartmentId { get; private set; }
    public DateOnly Today { get; private set; }
    public DateOnly WeekFrom => Today.AddDays(-((7 + (int)Today.DayOfWeek - 1) % 7));
    public DateOnly WeekTo => WeekFrom.AddDays(6);
    public DateOnly MonthFrom => new(Today.Year, Today.Month, 1);
    public DateOnly MonthTo => MonthFrom.AddMonths(1).AddDays(-1);
    public bool FiltersRestored { get; private set; }

    public async Task OnGet(int? employeeId, int? year, int? month, DateOnly? from, DateOnly? to, int? departmentId)
    {
        var hasQuery = Request.Query.Count > 0;
        var reset = Request.Query.ContainsKey("reset");
        IReadOnlyDictionary<string, string>? saved = null;
        if (!hasQuery)
        {
            saved = await _savedFilters.GetAsync(SavedFilterService.ReportsPage);
            FiltersRestored = saved.Count > 0;
        }

        Today = DateOnly.FromDateTime(DateTime.Today);
        EmployeeId = employeeId ?? SavedInt(saved, "employeeId");
        Year = ValidYear(year) ?? ValidYear(SavedInt(saved, "year")) ?? Today.Year;
        Month = ValidMonth(month) ?? ValidMonth(SavedInt(saved, "month")) ?? Today.Month;
        From = from ?? SavedDate(saved, "from") ?? MonthFrom;
        To = to ?? SavedDate(saved, "to") ?? MonthTo;
        DepartmentId = departmentId ?? SavedInt(saved, "departmentId");

        if (reset)
            await _savedFilters.ResetAsync(SavedFilterService.ReportsPage);
        else if (hasQuery)
            await SaveFiltersAsync(new Dictionary<string, string?>
            {
                ["employeeId"] = Number(EmployeeId),
                ["year"] = Number(Year),
                ["month"] = Number(Month),
                ["from"] = Date(From),
                ["to"] = Date(To),
                ["departmentId"] = Number(DepartmentId),
            });

        Employees = new SelectList(
            await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(), "Id", "FullName", EmployeeId);
        Departments = new SelectList(
            await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(), "Id", "Name", DepartmentId);
    }

    public async Task<IActionResult> OnGetEmployeeTimesheetAsync(int employeeId, int year, int month)
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null) return NotFound();
        await SaveFiltersAsync(new Dictionary<string, string?>
        {
            ["employeeId"] = Number(employeeId),
            ["year"] = Number(year),
            ["month"] = Number(month),
        });
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var days = await _reports.GetEmployeeDaysAsync(employeeId, from, to);
        var bytes = _excel.ExportEmployeeTimesheet(employee, from, to, days);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Табель_{employee.FullName}_{year}-{month:D2}.xlsx");
    }

    public async Task<IActionResult> OnGetBoatsAsync(DateOnly from, DateOnly to, int? departmentId)
    {
        await SaveFiltersAsync(new Dictionary<string, string?>
        {
            ["from"] = Date(from),
            ["to"] = Date(to),
            ["departmentId"] = Number(departmentId),
        });
        var rows = await _reports.GetBoatSummaryAsync(from, to, departmentId);
        string? deptName = null;
        if (departmentId is int id)
            deptName = (await _db.Departments.FindAsync(id))?.Name;
        var bytes = _excel.ExportBoatSummary(from, to, rows, deptName);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Сводка_по_катерам_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }

    public async Task<IActionResult> OnGetLoadAsync(DateOnly from, DateOnly to)
    {
        await SaveFiltersAsync(new Dictionary<string, string?>
        {
            ["from"] = Date(from),
            ["to"] = Date(to),
        });
        var rows = await _reports.GetEmployeeLoadAsync(from, to);
        var bytes = _excel.ExportEmployeeLoad(from, to, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Загрузка_сотрудников_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }

    private Task SaveFiltersAsync(IReadOnlyDictionary<string, string?> filters)
        => _savedFilters.SaveAsync(SavedFilterService.ReportsPage, filters);

    private static int? ValidYear(int? year) => year is >= 1 and <= 9999 ? year : null;

    private static int? ValidMonth(int? month) => month is >= 1 and <= 12 ? month : null;

    private static int? SavedInt(IReadOnlyDictionary<string, string>? saved, string key)
        => saved is not null && saved.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.None, NumberFormatInfo.InvariantInfo, out var number)
                ? number
                : null;

    private static DateOnly? SavedDate(IReadOnlyDictionary<string, string>? saved, string key)
        => saved is not null && saved.TryGetValue(key, out var value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
