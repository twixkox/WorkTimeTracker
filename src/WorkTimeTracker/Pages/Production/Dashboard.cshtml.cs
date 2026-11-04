using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Production;

public class DashboardModel : PageModel
{
    private readonly ReportService _reports;
    private readonly ExcelService _excel;

    public DashboardModel(ReportService reports, ExcelService excel)
    {
        _reports = reports;
        _excel = excel;
    }

    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }

    public string DeptChartJson { get; private set; } = "[]";
    public string BoatsChartJson { get; private set; } = "[]";
    public string LoadChartJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGet(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        From = from ?? today.AddDays(-(int)today.DayOfWeek + 1);
        if (From > today) From = today.AddDays(-6);
        To = to ?? From.AddDays(13);

        var dept = await _reports.GetDepartmentSummaryAsync(From, To);
        var boats = (await _reports.GetBoatSummaryAsync(From, To)).Take(15).ToList();
        var load = (await _reports.GetEmployeeLoadAsync(From, To))
            .OrderByDescending(l => l.WorkedHours).Take(15).ToList();

        DeptChartJson = JsonSerializer.Serialize(dept.Select(d => new
        {
            dept = d.DepartmentName,
            boats = d.BoatHours,
            inhouse = d.InhouseHours,
        }));

        BoatsChartJson = JsonSerializer.Serialize(boats.Select(b => new
        {
            boat = b.BoatNumber,
            hours = b.Hours,
        }));

        LoadChartJson = JsonSerializer.Serialize(load.Select(l => new
        {
            name = l.FullName,
            worked = l.WorkedHours,
        }));

        return Page();
    }

    public async Task<IActionResult> OnGetExportBoatsAsync(DateOnly from, DateOnly to)
    {
        var rows = await _reports.GetBoatSummaryAsync(from, to);
        var bytes = _excel.ExportBoatSummary(from, to, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Сводка_по_катерам_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }

    public async Task<IActionResult> OnGetExportLoadAsync(DateOnly from, DateOnly to)
    {
        var rows = await _reports.GetEmployeeLoadAsync(from, to);
        var bytes = _excel.ExportEmployeeLoad(from, to, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Загрузка_сотрудников_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx");
    }
}
