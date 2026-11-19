using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class EngineeringTaskServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly EngineeringTaskService _service;

    public EngineeringTaskServiceTests()
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
        _service = new EngineeringTaskService(_db, _userManager,
            new NotificationService(_db), new AuditService(_db));
    }

    [Theory]
    [InlineData(AppRoles.ProductionHead)]
    [InlineData(AppRoles.ChiefEngineer)]
    [InlineData(AppRoles.Admin)]
    public async Task Create_AllowedRole_SavesTaskAuditAndNotifiesOnlyActiveChiefEngineers(string role)
    {
        var creator = await CreateUserAsync("Создатель", role);
        var activeChief = await CreateUserAsync("Действующий главный инженер", AppRoles.ChiefEngineer);
        var inactiveChief = await CreateUserAsync("Недействующий главный инженер", AppRoles.ChiefEngineer, false);
        var boat = new Boat { Number = "К-10", IsActive = true };
        _db.Boats.Add(boat);
        await _db.SaveChangesAsync();
        var deadline = new DateOnly(2026, 12, 15);

        var result = await _service.CreateAsync(Actor(creator, role), "  Новая задача  ",
            EngineeringTaskPriority.High, boat.Id, "  Корпус-7  ", deadline);

        Assert.True(result.Success, result.Error);
        var task = Assert.Single(_db.EngineeringTasks);
        Assert.Equal("Новая задача", task.Title);
        Assert.Equal("Корпус-7", task.HullNumber);
        Assert.Equal(deadline, task.Deadline);
        Assert.Equal(EngineeringTaskStatus.New, task.Status);
        Assert.Equal(creator.Id, task.CreatedByUserId);
        Assert.Equal("Создатель", task.CreatedByName);
        Assert.Equal("Created", Assert.Single(_db.AuditLogs).Action);
        Assert.Contains(_db.Notifications, n => n.UserId == activeChief.Id && n.Title == "Новое задание КБ");
        Assert.DoesNotContain(_db.Notifications, n => n.UserId == inactiveChief.Id);
    }

    [Fact]
    public async Task Create_Engineer_IsRejectedWithoutSideEffects()
    {
        var engineer = await CreateUserAsync("Инженер", AppRoles.Engineer);

        var result = await _service.CreateAsync(Actor(engineer, AppRoles.Engineer), "Задача",
            EngineeringTaskPriority.Normal, null, null, null);

        Assert.False(result.Success);
        Assert.Empty(_db.EngineeringTasks);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Theory]
    [InlineData("", EngineeringTaskPriority.Normal)]
    [InlineData("Задача", (EngineeringTaskPriority)99)]
    public async Task Create_InvalidTitleOrPriority_IsRejected(string title, EngineeringTaskPriority priority)
    {
        var creator = await CreateUserAsync("Создатель", AppRoles.ProductionHead);

        var result = await _service.CreateAsync(Actor(creator, AppRoles.ProductionHead), title,
            priority, null, null, null);

        Assert.False(result.Success);
        Assert.NotNull(result.Field);
        Assert.Empty(_db.EngineeringTasks);
    }

    [Fact]
    public async Task Create_InactiveBoat_IsRejected()
    {
        var creator = await CreateUserAsync("Создатель", AppRoles.ProductionHead);
        var boat = new Boat { Number = "К-10", IsActive = false };
        _db.Boats.Add(boat);
        await _db.SaveChangesAsync();

        var result = await _service.CreateAsync(Actor(creator, AppRoles.ProductionHead), "Задача",
            EngineeringTaskPriority.Normal, boat.Id, null, null);

        Assert.False(result.Success);
        Assert.Equal("BoatId", result.Field);
        Assert.Empty(_db.EngineeringTasks);
    }

    [Theory]
    [InlineData(AppRoles.ChiefEngineer)]
    [InlineData(AppRoles.Admin)]
    public async Task Update_ChiefEngineerOrAdmin_AssignsActiveEngineerAndSavesFields(string role)
    {
        var editor = await CreateUserAsync("Редактор", role);
        var engineer = await CreateUserAsync("Инженер Петров", AppRoles.Engineer);
        var boat = new Boat { Number = "К-20" };
        _db.Boats.Add(boat);
        await _db.SaveChangesAsync();
        var task = await SeedTaskAsync();
        var input = ValidInput(task, engineer.Id);
        input.Title = "  Изменённая задача  ";
        input.Priority = EngineeringTaskPriority.Urgent;
        input.BoatId = boat.Id;
        input.PlannedDueOn = new DateOnly(2026, 12, 1);

        var result = await _service.UpdateAsync(Actor(editor, role), task.Id, input);

        Assert.True(result.Success, result.Error);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal("Изменённая задача", saved.Title);
        Assert.Equal(engineer.Id, saved.AssigneeId);
        Assert.Equal(EngineeringTaskStatus.Assigned, saved.Status);
        Assert.Equal(EngineeringTaskPriority.Urgent, saved.Priority);
        Assert.Equal(boat.Id, saved.BoatId);
        Assert.Equal(new DateOnly(2026, 12, 1), saved.PlannedDueOn);
        Assert.Equal("Updated", Assert.Single(_db.AuditLogs).Action);
        Assert.Equal(2, _db.Notifications.Count());
        Assert.Contains(_db.Notifications, n => n.Title == "Вам назначена задача КБ" && n.UserId == engineer.Id);
        Assert.Contains(_db.Notifications, n => n.Title == "Изменён приоритет задачи КБ" && n.UserId == engineer.Id);
    }

    [Theory]
    [InlineData(AppRoles.Engineer)]
    [InlineData(AppRoles.ProductionHead)]
    public async Task Update_NotChiefOrAdmin_IsRejectedWithoutChanges(string role)
    {
        var editor = await CreateUserAsync("Редактор", role);
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);
        var input = ValidInput(task, engineer.Id);
        input.Title = "Недопустимое изменение";

        var result = await _service.UpdateAsync(Actor(editor, role), task.Id, input);

        Assert.False(result.Success);
        _db.ChangeTracker.Clear();
        Assert.Equal("Исходная задача", (await _db.EngineeringTasks.SingleAsync()).Title);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Theory]
    [InlineData(AppRoles.Engineer, false)]
    [InlineData(AppRoles.ProductionHead, true)]
    public async Task Update_InactiveOrNonEngineerAssignee_IsRejected(string role, bool isActive)
    {
        var editor = await CreateUserAsync("Главный инженер", AppRoles.ChiefEngineer);
        var original = await CreateUserAsync("Исходный исполнитель", AppRoles.Engineer);
        var rejected = await CreateUserAsync("Неподходящий исполнитель", role, isActive);
        var task = await SeedTaskAsync(original, EngineeringTaskStatus.Assigned);
        var input = ValidInput(task, rejected.Id);

        var result = await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input);

        Assert.False(result.Success);
        Assert.Equal("AssigneeId", result.Field);
        _db.ChangeTracker.Clear();
        Assert.Equal(original.Id, (await _db.EngineeringTasks.SingleAsync()).AssigneeId);
    }

    [Fact]
    public async Task Update_StatusJumpAndDeadlineWithoutReason_AreRejected()
    {
        var editor = await CreateUserAsync("Главный инженер", AppRoles.ChiefEngineer);
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);
        var jump = ValidInput(task, engineer.Id);
        jump.Status = EngineeringTaskStatus.Done;

        var jumpResult = await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, jump);

        Assert.False(jumpResult.Success);
        Assert.Equal("Status", jumpResult.Field);
        jump.Status = EngineeringTaskStatus.Assigned;
        jump.Deadline = task.Deadline!.Value.AddDays(5);
        jump.DeadlineChangeComment = " ";

        var deadlineResult = await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, jump);

        Assert.False(deadlineResult.Success);
        Assert.Equal("DeadlineChangeComment", deadlineResult.Field);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(EngineeringTaskStatus.Assigned, saved.Status);
        Assert.Equal(task.Deadline, saved.Deadline);
    }

    [Fact]
    public async Task Update_DeadlineChange_ResetsRemindersAuditsAndNotifiesAssignee()
    {
        var editor = await CreateUserAsync("Главный инженер", AppRoles.ChiefEngineer);
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);
        task.DeadlineReminderSentOn = task.Deadline;
        task.OverdueReminderSentOn = task.Deadline;
        await _db.SaveChangesAsync();
        var input = ValidInput(task, engineer.Id);
        input.Deadline = task.Deadline!.Value.AddDays(10);
        input.DeadlineChangeComment = "  Новый производственный план  ";

        var result = await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input);

        Assert.True(result.Success, result.Error);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Null(saved.DeadlineReminderSentOn);
        Assert.Null(saved.OverdueReminderSentOn);
        Assert.Equal(2, _db.AuditLogs.Count());
        Assert.Contains(_db.AuditLogs, a => a.Action == "DeadlineChanged"
            && a.Details!.Contains("Новый производственный план"));
        Assert.Contains(_db.Notifications, n => n.UserId == engineer.Id && n.Title == "Изменён дедлайн задачи КБ");
    }

    [Fact]
    public async Task Start_CurrentEngineerFromAssigned_SetsStatusStartedOnAndAudit()
    {
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);

        var result = await _service.StartAsync(Actor(engineer, AppRoles.Engineer), task.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal(EngineeringTaskStatus.InProgress, result.Task!.Status);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), result.Task.StartedOn);
        Assert.Equal("Started", Assert.Single(_db.AuditLogs).Action);
    }

    [Theory]
    [InlineData(AppRoles.Engineer)]
    [InlineData(AppRoles.ChiefEngineer)]
    [InlineData(AppRoles.Admin)]
    public async Task Start_OtherEngineerChiefOrAdmin_IsRejected(string role)
    {
        var assignee = await CreateUserAsync("Назначенный исполнитель", AppRoles.Engineer);
        var other = await CreateUserAsync("Другой пользователь", role);
        var task = await SeedTaskAsync(assignee, EngineeringTaskStatus.Assigned);

        var result = await _service.StartAsync(Actor(other, role), task.Id);

        Assert.False(result.Success);
        _db.ChangeTracker.Clear();
        Assert.Equal(EngineeringTaskStatus.Assigned, (await _db.EngineeringTasks.SingleAsync()).Status);
        Assert.Empty(_db.AuditLogs);
    }

    [Theory]
    [InlineData(EngineeringTaskStatus.New)]
    [InlineData(EngineeringTaskStatus.InProgress)]
    [InlineData(EngineeringTaskStatus.Done)]
    public async Task Start_OnlyAssignedStatusIsAllowed(EngineeringTaskStatus status)
    {
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, status);

        var result = await _service.StartAsync(Actor(engineer, AppRoles.Engineer), task.Id);

        Assert.False(result.Success);
        Assert.Equal("Status", result.Field);
    }

    [Fact]
    public async Task Complete_CurrentEngineerFromInProgress_SetsCompletedDateTimeAuditAndNotifiesStakeholders()
    {
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var creator = await CreateUserAsync("Автор", AppRoles.ProductionHead);
        var chief = await CreateUserAsync("Главный инженер", AppRoles.ChiefEngineer);
        var inactiveChief = await CreateUserAsync("Неактивный главный инженер", AppRoles.ChiefEngineer, false);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.InProgress);
        task.CreatedByUserId = creator.Id;
        await _db.SaveChangesAsync();
        var before = DateTime.Now.AddSeconds(-1);

        var result = await _service.CompleteAsync(Actor(engineer, AppRoles.Engineer), task.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal(EngineeringTaskStatus.Done, result.Task!.Status);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), result.Task.CompletedOn);
        Assert.NotNull(result.Task.CompletedAt);
        Assert.InRange(result.Task.CompletedAt!.Value, before, DateTime.Now.AddSeconds(1));
        Assert.Equal("Completed", Assert.Single(_db.AuditLogs).Action);
        Assert.Equal(2, _db.Notifications.Count());
        Assert.Contains(_db.Notifications, n => n.UserId == creator.Id && n.Title == "Задача КБ выполнена");
        Assert.Contains(_db.Notifications, n => n.UserId == chief.Id
            && n.Link == $"/Engineering/Details/{task.Id}"
            && n.Message.Contains("Исполнитель")
            && n.Message.Contains(result.Task.CompletedAt.Value.ToString("dd.MM.yyyy HH:mm")));
        Assert.DoesNotContain(_db.Notifications, n => n.UserId == engineer.Id || n.UserId == inactiveChief.Id);
    }

    [Theory]
    [InlineData(EngineeringTaskStatus.Assigned)]
    [InlineData(EngineeringTaskStatus.Done)]
    public async Task Complete_OnlyInProgressStatusIsAllowed(EngineeringTaskStatus status)
    {
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, status);

        var result = await _service.CompleteAsync(Actor(engineer, AppRoles.Engineer), task.Id);

        Assert.False(result.Success);
        Assert.Equal("Status", result.Field);
    }

    [Fact]
    public async Task InactiveEngineer_CannotStartCompleteOrChangePlannedDue()
    {
        var engineer = await CreateUserAsync("Неактивный исполнитель", AppRoles.Engineer, false);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.InProgress);
        var actor = Actor(engineer, AppRoles.Engineer);

        Assert.False((await _service.StartAsync(actor, task.Id)).Success);
        Assert.False((await _service.CompleteAsync(actor, task.Id)).Success);
        Assert.False((await _service.SetPlannedDueAsync(actor, task.Id, DateOnly.FromDateTime(DateTime.Today))).Success);
        Assert.Empty(_db.AuditLogs);
    }

    [Fact]
    public async Task SetPlannedDue_CurrentEngineer_SavesAndAuditsButOtherEngineerCannotChange()
    {
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var other = await CreateUserAsync("Другой исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);
        var planned = new DateOnly(2026, 12, 20);

        Assert.False((await _service.SetPlannedDueAsync(Actor(other, AppRoles.Engineer), task.Id, planned)).Success);
        var result = await _service.SetPlannedDueAsync(Actor(engineer, AppRoles.Engineer), task.Id, planned);

        Assert.True(result.Success, result.Error);
        Assert.Equal(planned, result.Task!.PlannedDueOn);
        Assert.Equal("PlannedDueChanged", Assert.Single(_db.AuditLogs).Action);
    }

    [Fact]
    public async Task Update_CompleteAndReopen_SetsAndClearsCompletedAt()
    {
        var editor = await CreateUserAsync("Главный инженер", AppRoles.ChiefEngineer);
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);
        var input = ValidInput(task, engineer.Id);
        input.Status = EngineeringTaskStatus.InProgress;
        Assert.True((await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input)).Success);

        _db.ChangeTracker.Clear();
        input.Status = EngineeringTaskStatus.Done;
        Assert.True((await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input)).Success);
        _db.ChangeTracker.Clear();
        var completed = await _db.EngineeringTasks.SingleAsync();
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), completed.CompletedOn);

        input.Status = EngineeringTaskStatus.InProgress;
        Assert.True((await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input)).Success);
        _db.ChangeTracker.Clear();
        var reopened = await _db.EngineeringTasks.SingleAsync();
        Assert.Null(reopened.CompletedAt);
        Assert.Null(reopened.CompletedOn);
    }

    [Fact]
    public async Task Update_Completion_NotifiesAssigneeOnceWhenAlreadyDoneSavedAgain()
    {
        var editor = await CreateUserAsync("Главный инженер", AppRoles.ChiefEngineer);
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var task = await SeedTaskAsync(engineer, EngineeringTaskStatus.InProgress);
        var input = ValidInput(task, engineer.Id);
        input.Status = EngineeringTaskStatus.Done;

        Assert.True((await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input)).Success);
        Assert.True((await _service.UpdateAsync(Actor(editor, AppRoles.ChiefEngineer), task.Id, input)).Success);

        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(engineer.Id, notification.UserId);
        Assert.Equal("Задача КБ выполнена", notification.Title);
        Assert.Equal($"/Engineering/Details/{task.Id}", notification.Link);
    }

    [Theory]
    [InlineData(AppRoles.Engineer)]
    [InlineData(AppRoles.ChiefEngineer)]
    [InlineData(AppRoles.ProductionHead)]
    [InlineData(AppRoles.Admin)]
    public async Task AddComment_EngineeringAccessRole_SavesTrimmedCommentAndAudit(string role)
    {
        var author = await CreateUserAsync("Автор", role);
        var task = await SeedTaskAsync();

        var result = await _service.AddCommentAsync(Actor(author, role), task.Id, "  Комментарий к задаче  ");

        Assert.True(result.Success, result.Error);
        var comment = Assert.Single(_db.EngineeringTaskComments);
        Assert.Equal(task.Id, comment.EngineeringTaskId);
        Assert.Equal(author.Id, comment.AuthorUserId);
        Assert.Equal("Автор", comment.AuthorName);
        Assert.Equal("Комментарий к задаче", comment.Text);
        Assert.Equal("CommentAdded", Assert.Single(_db.AuditLogs).Action);
    }

    [Fact]
    public async Task AddComment_UnauthorizedEmptyOrTooLong_IsRejected()
    {
        var user = await CreateUserAsync("Пользователь", AppRoles.Employee);
        var task = await SeedTaskAsync();

        Assert.False((await _service.AddCommentAsync(Actor(user, AppRoles.Employee), task.Id, "Недоступно")).Success);
        var chief = Actor(user, AppRoles.ChiefEngineer);
        Assert.False((await _service.AddCommentAsync(chief, task.Id, " ")).Success);
        Assert.False((await _service.AddCommentAsync(chief, task.Id, new string('К', 4001))).Success);
        Assert.Empty(_db.EngineeringTaskComments);
        Assert.Empty(_db.AuditLogs);
    }

    [Fact]
    public async Task GetTaskAndUserTasks_ReturnDetailsAndSeparateActiveFromCompleted()
    {
        var engineer = await CreateUserAsync("Исполнитель", AppRoles.Engineer);
        var active = await SeedTaskAsync(engineer, EngineeringTaskStatus.Assigned);
        var completed = await SeedTaskAsync(engineer, EngineeringTaskStatus.Done);
        await SeedTaskAsync(null, EngineeringTaskStatus.New);
        _db.EngineeringTaskComments.Add(new EngineeringTaskComment
        {
            EngineeringTaskId = active.Id,
            AuthorUserId = engineer.Id,
            AuthorName = engineer.DisplayName,
            Text = "Комментарий",
        });
        await _db.SaveChangesAsync();

        var details = await _service.GetTaskAsync(Actor(engineer, AppRoles.Engineer), active.Id);
        var activeTasks = await _service.GetUserTasksAsync(engineer.Id, false);
        var completedTasks = await _service.GetUserTasksAsync(engineer.Id, true);

        Assert.True(details.Success, details.Error);
        Assert.Single(details.Task!.Comments);
        Assert.Equal(active.Id, Assert.Single(activeTasks).Id);
        Assert.Equal(completed.Id, Assert.Single(completedTasks).Id);
        var employee = await CreateUserAsync("Сотрудник", AppRoles.Employee);
        Assert.False((await _service.GetTaskAsync(Actor(employee, AppRoles.Employee), active.Id)).Success);
    }

    private async Task<ApplicationUser> CreateUserAsync(string displayName, string role, bool isActive = true)
    {
        if (!await _roleManager.RoleExistsAsync(role))
            Assert.True((await _roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            IsActive = isActive,
        };
        var result = await _userManager.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        Assert.True((await _userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private async Task<EngineeringTask> SeedTaskAsync(ApplicationUser? assignee = null,
        EngineeringTaskStatus status = EngineeringTaskStatus.New)
    {
        var task = new EngineeringTask
        {
            CreatedByUserId = "creator",
            CreatedByName = "Создатель",
            Title = "Исходная задача",
            Priority = EngineeringTaskPriority.Normal,
            Status = status,
            AssigneeId = assignee?.Id,
            Deadline = new DateOnly(2026, 12, 10),
            StartedOn = status >= EngineeringTaskStatus.InProgress ? new DateOnly(2026, 12, 1) : null,
            CompletedOn = status == EngineeringTaskStatus.Done ? new DateOnly(2026, 12, 5) : null,
            CompletedAt = status == EngineeringTaskStatus.Done ? new DateTime(2026, 12, 5, 12, 0, 0) : null,
        };
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    private static EngineeringTaskUpdateInput ValidInput(EngineeringTask task, string? assigneeId) => new()
    {
        Title = task.Title,
        Priority = task.Priority,
        Status = task.Status,
        AssigneeId = assigneeId,
        BoatId = task.BoatId,
        PlannedDueOn = task.PlannedDueOn,
        Deadline = task.Deadline,
    };

    private static EngineeringTaskActor Actor(ApplicationUser user, string role)
        => new(user.Id, user.DisplayName, new[] { role });

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
