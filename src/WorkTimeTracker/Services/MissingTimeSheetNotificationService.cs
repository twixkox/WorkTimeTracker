using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public class MissingTimeSheetNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifications;

    public MissingTimeSheetNotificationService(ApplicationDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<int> SendDailySummariesAsync(DateOnly date)
    {
        var sent = 0;
        var departments = await _db.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();

        foreach (var department in departments)
        {
            var masters = await (
                from employee in _db.Employees
                join userRole in _db.UserRoles on employee.UserId equals userRole.UserId
                join role in _db.Roles on userRole.RoleId equals role.Id
                where employee.DepartmentId == department.Id && employee.IsActive &&
                      employee.UserId != null && role.Name == AppRoles.SectionMaster
                select employee.UserId!)
                .Distinct()
                .ToListAsync();
            if (masters.Count == 0) continue;

            var filledEmployeeIds = await _db.WorkDays
                .Where(w => w.Employee!.DepartmentId == department.Id && w.Date == date)
                .Select(w => w.EmployeeId)
                .ToListAsync();
            var missing = await _db.Employees
                .Where(e => e.DepartmentId == department.Id && e.IsActive && !filledEmployeeIds.Contains(e.Id))
                .OrderBy(e => e.FullName)
                .Select(e => e.FullName)
                .ToListAsync();
            if (missing.Count == 0) continue;

            var title = $"Незаполненные табели за {date:dd.MM.yyyy}";
            var link = $"/Master/Index?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}";
            var message = $"{department.Name}: не заполнили табель ({missing.Count}): {string.Join("; ", missing)}.";

            foreach (var masterId in masters)
            {
                var alreadySent = await _db.Notifications.AnyAsync(n =>
                    n.UserId == masterId && n.Title == title && n.Link == link);
                if (alreadySent) continue;
                await _notifications.NotifyAsync(masterId, title, message, link);
                sent++;
            }
        }

        return sent;
    }
}
