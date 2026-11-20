using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class PersonalDashboardServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0);
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public PersonalDashboardServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Employee_GetsMonthHoursActionableAndMissingWeekdays()
    {
        var employee = new Employee
        {
            FullName = "Работник",
            UserId = "current-user",
            Department = new Department { Name = "Сборка", IsWeeklyView = true },
        };
        _db.WorkDays.AddRange(
            Day(employee, 2, TimeSheetStatus.Approved, 8),
            Day(employee, 3, TimeSheetStatus.Submitted, 8),
            Day(employee, 4, TimeSheetStatus.Rejected, 4, "Уточнить работу"),
            Day(employee, 5, TimeSheetStatus.Draft, 7));
        await _db.SaveChangesAsync();

        var result = await Service(AppRoles.Employee).GetDashboardAsync(Now);

        var personal = result.Personal!;
        Assert.NotNull(personal);
        Assert.Equal(27, personal.MonthHours);
        Assert.Single(personal.PendingDays);
        var rejected = Assert.Single(personal.RejectedDays);
        Assert.Equal("Уточнить работу", rejected.RejectComment);
        Assert.Equal(11, personal.MissingDays.Count);
        Assert.Contains(new DateOnly(2026, 11, 20), personal.MissingDays);
        Assert.DoesNotContain(personal.MissingDays, d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.Equal("/Employee/Week?week=2026-11-04", personal.ContinueUrl);
        Assert.Null(result.Section);
        Assert.Null(result.Production);
        Assert.Null(result.Engineering);
    }

    [Fact]
    public async Task SectionMaster_QueueAndMissingEmployeesAreLimitedToOwnDepartment()
    {
        var ownDepartment = new Department { Name = "Сборка" };
        var otherDepartment = new Department { Name = "Склад" };
        var master = new Employee { FullName = "Мастер", UserId = "current-user", Department = ownDepartment };
        var worker = new Employee { FullName = "Сотрудник участка", Department = ownDepartment };
        var otherWorker = new Employee { FullName = "Другой участок", Department = otherDepartment };
        _db.WorkDays.AddRange(
            Day(master, 20, TimeSheetStatus.Recorded, 8),
            Day(worker, 19, TimeSheetStatus.Submitted, 8, submittedAt: Now.AddHours(-25)),
            Day(otherWorker, 19, TimeSheetStatus.Submitted, 8, submittedAt: Now.AddHours(-30)));
        await _db.SaveChangesAsync();

        var result = await Service(AppRoles.SectionMaster).GetDashboardAsync(Now);

        var section = result.Section!;
        Assert.NotNull(section);
        Assert.Equal(ownDepartment.Id, section.DepartmentId);
        var queueDay = Assert.Single(section.Queue);
        Assert.Equal(worker.Id, queueDay.EmployeeId);
        Assert.Equal(1, section.OverdueCount);
        Assert.Equal("Сотрудник участка", Assert.Single(section.MissingEmployees));
        Assert.Equal(8, result.Personal!.MonthHours);
    }

    [Theory]
    [InlineData(AppRoles.ProductionHead)]
    [InlineData(AppRoles.Admin)]
    public async Task ProductionOrAdmin_GetsAllDepartmentQueuesAndMonthKpi(string role)
    {
        var worker = new Employee { FullName = "Сборщик", Department = new Department { Name = "Сборка" } };
        var storekeeper = new Employee { FullName = "Кладовщик", Department = new Department { Name = "Склад" } };
        _db.WorkDays.AddRange(
            Day(worker, 2, TimeSheetStatus.Approved, 8),
            Day(worker, 3, TimeSheetStatus.Rejected, 4),
            Day(storekeeper, 19, TimeSheetStatus.Submitted, 8, submittedAt: Now.AddHours(-26)));
        await _db.SaveChangesAsync();

        var result = await Service(role).GetDashboardAsync(Now);

        var production = result.Production!;
        Assert.NotNull(production);
        Assert.Equal(1, production.ApprovedDays);
        Assert.Equal(20, production.MonthHours);
        Assert.Equal(1, production.RejectedDays);
        Assert.Equal(1, production.OverdueCount);
        Assert.Equal(2, production.MissingEmployees.Count);
        Assert.Equal(4, production.ProblemCount);
        var queueDay = Assert.Single(production.Queue);
        Assert.Equal("Склад", queueDay.DepartmentName);
        Assert.Null(result.Personal);
        Assert.Null(result.Section);
        Assert.NotNull(result.Engineering);
    }

    [Fact]
    public async Task Engineer_GetsOnlyOwnActiveTasks()
    {
        AddTaskAssignees();
        _db.EngineeringTasks.AddRange(
            Task("Моя просроченная", "current-user", new DateOnly(2026, 11, 19)),
            Task("Чужая", "other-user", NowDate().AddDays(1)),
            Task("Неназначенная", null, NowDate().AddDays(1)),
            Task("Завершённая", "current-user", NowDate().AddDays(-2), EngineeringTaskStatus.Done));
        await _db.SaveChangesAsync();

        var result = await Service(AppRoles.Engineer).GetDashboardAsync(Now);

        var engineering = result.Engineering!;
        Assert.NotNull(engineering);
        Assert.True(engineering.AssignedOnly);
        Assert.Equal(1, engineering.ActiveCount);
        Assert.Equal(1, engineering.OverdueCount);
        Assert.Equal(0, engineering.DueSoonCount);
        Assert.Equal(0, engineering.UnassignedCount);
        Assert.Equal("Моя просроченная", Assert.Single(engineering.Tasks).Title);
    }

    [Fact]
    public async Task ChiefEngineer_GetsAllActiveTasksAndDeadlineKpi()
    {
        AddTaskAssignees();
        _db.EngineeringTasks.AddRange(
            Task("Просроченная", "current-user", NowDate().AddDays(-1)),
            Task("Срок сегодня", "other-user", NowDate()),
            Task("Срок послезавтра", null, NowDate().AddDays(2)),
            Task("Не срочная", null, NowDate().AddDays(3)),
            Task("Завершённая", null, NowDate().AddDays(-2), EngineeringTaskStatus.Done));
        await _db.SaveChangesAsync();

        var result = await Service(AppRoles.ChiefEngineer).GetDashboardAsync(Now);

        var engineering = result.Engineering!;
        Assert.NotNull(engineering);
        Assert.False(engineering.AssignedOnly);
        Assert.Equal(4, engineering.ActiveCount);
        Assert.Equal(1, engineering.OverdueCount);
        Assert.Equal(2, engineering.DueSoonCount);
        Assert.Equal(2, engineering.UnassignedCount);
        Assert.Equal(4, engineering.Tasks.Count);
        Assert.Equal("Просроченная", engineering.Tasks[0].Title);
    }

    [Fact]
    public void DashboardPage_RequiresAuthentication()
    {
        Assert.True(Attribute.IsDefined(typeof(Pages.Dashboard.IndexModel),
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)));
    }

    [Fact]
    public async Task DashboardPage_LoadsCurrentUserDashboard()
    {
        var employee = new Employee
        {
            FullName = "Работник",
            UserId = "current-user",
            Department = new Department { Name = "Сборка" },
        };
        var today = DateOnly.FromDateTime(DateTime.Today);
        _db.WorkDays.Add(new WorkDay
        {
            Employee = employee,
            Date = today,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
            Status = TimeSheetStatus.Approved,
        });
        await _db.SaveChangesAsync();
        var model = new Pages.Dashboard.IndexModel(Service(AppRoles.Employee));

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(today, model.Dashboard.Today);
        Assert.Equal(8, model.Dashboard.Personal!.MonthHours);
    }

    private PersonalDashboardService Service(string role)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "current-user"),
                new Claim(ClaimTypes.Name, "Текущий пользователь"),
                new Claim(ClaimTypes.Role, role),
            }, "Test")),
        };
        return new PersonalDashboardService(_db,
            new CurrentUserService(new HttpContextAccessor { HttpContext = httpContext }, _db));
    }

    private static WorkDay Day(Employee employee, int monthDay, TimeSheetStatus status, decimal hours,
        string? rejectComment = null, DateTime? submittedAt = null)
    {
        var start = new TimeOnly(8, 0);
        return new WorkDay
        {
            Employee = employee,
            Date = new DateOnly(2026, 11, monthDay),
            StartTime = start,
            EndTime = start.AddHours((double)(hours + TimeCalculationService.LunchHours)),
            Status = status,
            RejectComment = rejectComment,
            SubmittedAt = submittedAt,
        };
    }

    private void AddTaskAssignees()
        => _db.Users.AddRange(
            new ApplicationUser { Id = "current-user", UserName = "current-user", DisplayName = "Текущий инженер" },
            new ApplicationUser { Id = "other-user", UserName = "other-user", DisplayName = "Другой инженер" });

    private static EngineeringTask Task(string title, string? assigneeId, DateOnly? deadline,
        EngineeringTaskStatus status = EngineeringTaskStatus.Assigned)
        => new()
        {
            Title = title,
            AssigneeId = assigneeId,
            Deadline = deadline,
            Status = status,
            CreatedByUserId = "creator",
            CreatedByName = "Руководитель",
            CreatedAt = Now,
        };

    private static DateOnly NowDate() => DateOnly.FromDateTime(Now);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
