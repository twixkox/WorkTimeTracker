using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Production;

/// <summary>Просмотр табелей одного участка начальником производства (все участки).</summary>
public class SectionModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ReportService _reports;
    private readonly ExcelService _excel;

    public SectionModel(ApplicationDbContext db, ReportService reports, ExcelService excel)
    {
        _db = db;
        _reports = reports;
        _excel = excel;
    }

    public Department? Department { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public List<(Models.Employee Employee, List<WorkDay> Days)> Rows { get; private set; } = new();

    public async Task<IActionResult> OnGet(int departmentId, DateOnly? from, DateOnly? to)
    {
        Department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId && d.IsActive);
        if (Department is null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? today.AddDays(-(int)today.DayOfWeek + 1);
        if (From > today) From = today.AddDays(-6);
        To = to ?? From.AddDays(13);

        Rows = await _reports.GetSectionDaysAsync(departmentId, From, To);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(int departmentId, DateOnly from, DateOnly to)
    {
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == departmentId);
        if (dept is null) return NotFound();
        var rows = await _reports.GetSectionDaysAsync(departmentId, from, to);
        var boats = await _reports.GetBoatSummaryAsync(from, to, departmentId);
        var bytes = _excel.ExportSectionTimesheet(dept, from, to, rows, boats);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Табель_{dept.Name}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }
}
