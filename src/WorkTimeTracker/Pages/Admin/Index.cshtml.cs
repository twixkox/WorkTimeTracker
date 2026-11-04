using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public int EmployeesCount { get; private set; }
    public int DepartmentsCount { get; private set; }
    public int BoatsCount { get; private set; }
    public int UsersCount { get; private set; }
    public int DaysThisMonth { get; private set; }
    public int PendingApproval { get; private set; }
    public int AuditCount { get; private set; }

    public async Task OnGet()
    {
        var monthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        EmployeesCount = await _db.Employees.CountAsync(e => e.IsActive);
        DepartmentsCount = await _db.Departments.CountAsync(d => d.IsActive);
        BoatsCount = await _db.Boats.CountAsync(b => b.IsActive);
        UsersCount = await _db.Users.CountAsync();
        DaysThisMonth = await _db.WorkDays.CountAsync(w => w.Date >= monthStart);
        PendingApproval = await _db.WorkDays.CountAsync(w => w.Status == TimeSheetStatus.Submitted);
        AuditCount = await _db.AuditLogs.CountAsync();
    }
}
