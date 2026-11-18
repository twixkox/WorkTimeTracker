using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class EngineeringTaskReminderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EngineeringTaskReminderServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task SendReminders_SendsUpcomingDeadlineToAssigneeAndActiveChiefEngineersOnlyOnce()
    {
        var today = new DateOnly(2026, 11, 18);
        var chief = await CreateUserAsync("chief", AppRoles.ChiefEngineer);
        var inactiveChief = await CreateUserAsync("inactive-chief", AppRoles.ChiefEngineer, isActive: false);
        var assignee = await CreateUserAsync("engineer", AppRoles.Engineer);
        var assignedTask = Task("Чертёж", today.AddDays(2), EngineeringTaskStatus.Assigned, assignee.Id);
        var unassignedTask = Task("Расчёт", today, EngineeringTaskStatus.New);
        var completedTask = Task("Завершённая", today, EngineeringTaskStatus.Done, assignee.Id);
        var distantTask = Task("Не срочная", today.AddDays(3), EngineeringTaskStatus.Assigned, assignee.Id);
        _db.EngineeringTasks.AddRange(assignedTask, unassignedTask, completedTask, distantTask);
        await _db.SaveChangesAsync();
        var service = CreateService();

        var sent = await service.SendRemindersAsync(today);

        Assert.Equal(3, sent);
        Assert.Equal(3, await _db.Notifications.CountAsync());
        Assert.Contains(_db.Notifications, n => n.UserId == assignee.Id
            && n.Title == "Приближается дедлайн задачи КБ"
            && n.Message.Contains("Чертёж")
            && n.Link == "/Engineering/Index");
        Assert.Contains(_db.Notifications, n => n.UserId == chief.Id
            && n.Title == "Приближается дедлайн задачи КБ"
            && n.Message.Contains("Чертёж"));
        Assert.Contains(_db.Notifications, n => n.UserId == chief.Id
            && n.Title == "Сегодня дедлайн задачи КБ"
            && n.Message.Contains("Расчёт"));
        Assert.DoesNotContain(_db.Notifications, n => n.UserId == inactiveChief.Id);
        Assert.DoesNotContain(_db.Notifications, n => n.Message.Contains("Завершённая")
            || n.Message.Contains("Не срочная"));
        Assert.Equal(assignedTask.Deadline, assignedTask.DeadlineReminderSentOn);
        Assert.Equal(unassignedTask.Deadline, unassignedTask.DeadlineReminderSentOn);
        Assert.Null(completedTask.DeadlineReminderSentOn);
        Assert.Null(distantTask.DeadlineReminderSentOn);

        Assert.Equal(0, await service.SendRemindersAsync(today));
        Assert.Equal(3, await _db.Notifications.CountAsync());
    }

    [Fact]
    public async Task SendReminders_SendsOverdueDeadlineWithDaysAndDoesNotRepeatIt()
    {
        var today = new DateOnly(2026, 11, 18);
        var chief = await CreateUserAsync("chief", AppRoles.ChiefEngineer);
        var assignee = await CreateUserAsync("engineer", AppRoles.Engineer);
        var task = Task("Просроченный чертёж", today.AddDays(-3), EngineeringTaskStatus.InProgress, assignee.Id);
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync();
        var service = CreateService();

        var sent = await service.SendRemindersAsync(today);

        Assert.Equal(2, sent);
        Assert.Equal(2, await _db.Notifications.CountAsync(n =>
            n.Title == "Просрочен дедлайн задачи КБ" && n.Message.Contains("просрочка: 3 дн.")));
        Assert.Contains(_db.Notifications, n => n.UserId == chief.Id);
        Assert.Contains(_db.Notifications, n => n.UserId == assignee.Id);
        Assert.Equal(task.Deadline, task.OverdueReminderSentOn);
        Assert.Null(task.DeadlineReminderSentOn);

        Assert.Equal(0, await service.SendRemindersAsync(today));
        Assert.Equal(2, await _db.Notifications.CountAsync());
    }

    private EngineeringTaskReminderService CreateService()
        => new(_db, new NotificationService(_db), _userManager);

    private static EngineeringTask Task(string title, DateOnly deadline, EngineeringTaskStatus status,
        string? assigneeId = null)
        => new()
        {
            CreatedByUserId = "creator",
            CreatedByName = "Начальник производства",
            Title = title,
            Deadline = deadline,
            Status = status,
            AssigneeId = assigneeId,
        };

    private async Task<ApplicationUser> CreateUserAsync(string userName, string role, bool isActive = true)
    {
        if (!await _roleManager.RoleExistsAsync(role))
            Assert.True((await _roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            DisplayName = userName,
            IsActive = isActive,
        };
        Assert.True((await _userManager.CreateAsync(user)).Succeeded);
        Assert.True((await _userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
