using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public record BoatSummaryRow(string BoatNumber, string? BoatName, string? Customer, decimal Hours, int EntriesCount);
public record EmployeeLoadRow(int EmployeeId, string FullName, string DepartmentName, decimal WorkedHours, decimal BoatHours, decimal InhouseHours, int DaysFilled, int DaysApproved);
public record DepartmentSummaryRow(int DepartmentId, string DepartmentName, decimal BoatHours, decimal InhouseHours, decimal WorkedHours);

/// <summary>Агрегации и сводные отчёты за период.</summary>
public class ReportService
{
    private readonly ApplicationDbContext _db;

    public ReportService(ApplicationDbContext db) => _db = db;

    private IQueryable<WorkDay> DaysInPeriod(DateOnly from, DateOnly to, int? departmentId = null, int? employeeId = null)
    {
        var q = _db.WorkDays
            .Include(w => w.Employee).ThenInclude(e => e.Department)
            .Include(w => w.TimeEntries).ThenInclude(t => t.Boat)
            .Where(w => w.Date >= from && w.Date <= to);
        if (departmentId is not null)
            q = q.Where(w => w.Employee != null && w.Employee.DepartmentId == departmentId);
        if (employeeId is not null)
            q = q.Where(w => w.EmployeeId == employeeId);
        return q;
    }

    /// <summary>Трудозатраты по катерам за период (только записи с катером).</summary>
    public async Task<List<BoatSummaryRow>> GetBoatSummaryAsync(DateOnly from, DateOnly to, int? departmentId = null)
    {
        var days = await DaysInPeriod(from, to, departmentId).ToListAsync();
        return days.SelectMany(d => d.TimeEntries)
            .Where(t => t.BoatId is not null)
            .GroupBy(t => new { t.BoatId, t.Boat!.Number, t.Boat.Name, t.Boat.Customer })
            .Select(g => new BoatSummaryRow(g.Key.Number, g.Key.Name, g.Key.Customer,
                Math.Round(g.Sum(x => x.Hours), 2), g.Count()))
            .OrderByDescending(r => r.Hours)
            .ToList();
    }

    /// <summary>Внутрицеховые работы за период: сумма часов и количество записей.</summary>
    public async Task<(decimal Hours, int Count)> GetInhouseSummaryAsync(DateOnly from, DateOnly to, int? departmentId = null)
    {
        var days = await DaysInPeriod(from, to, departmentId).ToListAsync();
        var entries = days.SelectMany(d => d.TimeEntries).Where(t => t.IsInhouse).ToList();
        return (Math.Round(entries.Sum(e => e.Hours), 2), entries.Count);
    }

    /// <summary>Загрузка сотрудников за период.</summary>
    public async Task<List<EmployeeLoadRow>> GetEmployeeLoadAsync(DateOnly from, DateOnly to, int? departmentId = null)
    {
        var days = await DaysInPeriod(from, to, departmentId).ToListAsync();
        return days.GroupBy(d => new { d.EmployeeId, Name = d.Employee!.FullName, Dept = d.Employee!.Department!.Name })
            .Select(g => new EmployeeLoadRow(
                g.Key.EmployeeId, g.Key.Name, g.Key.Dept,
                Math.Round(g.Sum(d => d.WorkedHours), 2),
                Math.Round(g.SelectMany(d => d.TimeEntries).Where(t => !t.IsInhouse).Sum(t => t.Hours), 2),
                Math.Round(g.SelectMany(d => d.TimeEntries).Where(t => t.IsInhouse).Sum(t => t.Hours), 2),
                g.Count(),
                g.Count(d => d.Status == TimeSheetStatus.Approved)))
            .OrderBy(r => r.DepartmentName).ThenBy(r => r.FullName)
            .ToList();
    }

    /// <summary>Распределение времени по подразделениям.</summary>
    public async Task<List<DepartmentSummaryRow>> GetDepartmentSummaryAsync(DateOnly from, DateOnly to)
    {
        var days = await DaysInPeriod(from, to).ToListAsync();
        return days.GroupBy(d => new { d.Employee!.DepartmentId, d.Employee!.Department!.Name })
            .Select(g => new DepartmentSummaryRow(
                g.Key.DepartmentId, g.Key.Name,
                Math.Round(g.SelectMany(d => d.TimeEntries).Where(t => !t.IsInhouse).Sum(t => t.Hours), 2),
                Math.Round(g.SelectMany(d => d.TimeEntries).Where(t => t.IsInhouse).Sum(t => t.Hours), 2),
                Math.Round(g.Sum(d => d.WorkedHours), 2)))
            .OrderBy(r => r.DepartmentName)
            .ToList();
    }

    /// <summary>Дни конкретного сотрудника за период (для табеля за месяц).</summary>
    public Task<List<WorkDay>> GetEmployeeDaysAsync(int employeeId, DateOnly from, DateOnly to)
        => DaysInPeriod(from, to, employeeId: employeeId).OrderBy(d => d.Date).ToListAsync();

    /// <summary>Статусы табелей сотрудников участка за период (экран мастера).</summary>
    public async Task<List<(Employee Employee, List<WorkDay> Days)>> GetSectionDaysAsync(int departmentId, DateOnly from, DateOnly to)
    {
        var employees = await _db.Employees
            .Where(e => e.DepartmentId == departmentId && e.IsActive)
            .OrderBy(e => e.FullName)
            .ToListAsync();
        var days = await _db.WorkDays
            .Include(w => w.TimeEntries)
            .Where(w => w.Employee!.DepartmentId == departmentId && w.Date >= from && w.Date <= to)
            .ToListAsync();
        return employees
            .Select(e => (e, days.Where(d => d.EmployeeId == e.Id).OrderBy(d => d.Date).ToList()))
            .ToList();
    }
}
