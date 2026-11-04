using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class MissingTimeSheetNotificationServiceTests
{
    [Fact]
    public async Task SendDailySummaries_SendsOneSummaryWithOnlyMissingEmployees()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var date = new DateOnly(2026, 11, 4);
        var department = new Department { Name = "Сборка" };
        var role = new IdentityRole(AppRoles.SectionMaster);
        var masterUser = new ApplicationUser { Id = "master-user", UserName = "master@test.local" };
        db.AddRange(department, role, masterUser);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = masterUser.Id, RoleId = role.Id });

        var master = new Employee { FullName = "Мастер", DepartmentId = department.Id, UserId = masterUser.Id };
        var filled = new Employee { FullName = "Заполнил", DepartmentId = department.Id };
        var missing = new Employee { FullName = "Не заполнил", DepartmentId = department.Id };
        var inactive = new Employee { FullName = "Неактивный", DepartmentId = department.Id, IsActive = false };
        db.Employees.AddRange(master, filled, missing, inactive);
        await db.SaveChangesAsync();
        db.WorkDays.AddRange(
            new WorkDay { EmployeeId = master.Id, Date = date },
            new WorkDay { EmployeeId = filled.Id, Date = date });
        await db.SaveChangesAsync();

        var service = new MissingTimeSheetNotificationService(db, new NotificationService(db));
        var sent = await service.SendDailySummariesAsync(date);

        Assert.Equal(1, sent);
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(masterUser.Id, notification.UserId);
        Assert.Contains("Не заполнил", notification.Message);
        Assert.DoesNotContain("Заполнил", notification.Message);
        Assert.DoesNotContain("Неактивный", notification.Message);
        Assert.Contains("04.11.2026", notification.Title);
        Assert.Equal("/Master/Index?from=2026-11-04&to=2026-11-04", notification.Link);

        Assert.Equal(0, await service.SendDailySummariesAsync(date));
        Assert.Equal(1, await db.Notifications.CountAsync());
    }
}

public class ShiftDefaultsTests
{
    [Fact]
    public void ShiftDefaults_AreNineToEighteen()
    {
        Assert.Equal(new TimeOnly(9, 0), ShiftDefaults.Start);
        Assert.Equal(new TimeOnly(18, 0), ShiftDefaults.End);
    }
}
