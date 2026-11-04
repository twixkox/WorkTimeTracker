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

    public ReportsModel(ApplicationDbContext db, ReportService reports, ExcelService excel)
    {
        _db = db;
        _reports = reports;
        _excel = excel;
    }

    public SelectList Employees { get; private set; } = default!;
    public SelectList Departments { get; private set; } = default!;

    public async Task OnGet()
    {
        Employees = new SelectList(
            await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(), "Id", "FullName");
        Departments = new SelectList(
            await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(), "Id", "Name");
    }

    public async Task<IActionResult> OnGetEmployeeTimesheetAsync(int employeeId, int year, int month)
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null) return NotFound();
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var days = await _reports.GetEmployeeDaysAsync(employeeId, from, to);
        var bytes = _excel.ExportEmployeeTimesheet(employee, from, to, days);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Табель_{employee.FullName}_{year}-{month:D2}.xlsx");
    }

    public async Task<IActionResult> OnGetBoatsAsync(DateOnly from, DateOnly to, int? departmentId)
    {
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
        var rows = await _reports.GetEmployeeLoadAsync(from, to);
        var bytes = _excel.ExportEmployeeLoad(from, to, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Загрузка_сотрудников_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }
}
