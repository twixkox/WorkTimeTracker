using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Pages.Engineering;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class EngineeringTaskEditTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EngineeringTaskEditTests()
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

    [Theory]
    [InlineData(AppRoles.ChiefEngineer)]
    [InlineData(AppRoles.Admin)]
    public async Task ChiefEngineerOrAdmin_AssignsActiveEngineerAndSavesEditableFields(string role)
    {
        var engineer = await CreateUserAsync("Инженер Петров", AppRoles.Engineer);
        var boat = new Boat { Number = "К-204", Name = "Новый катер" };
        _db.Boats.Add(boat);
        await _db.SaveChangesAsync();
        var task = await SeedTaskAsync();
        var model = CreateModel(role);
        model.Title = "  Разработка нового чертежа  ";
        model.Priority = EngineeringTaskPriority.Normal;
        model.Status = EngineeringTaskStatus.Assigned;
        model.AssigneeId = engineer.Id;
        model.BoatId = boat.Id;
        model.PlannedDueOn = new DateOnly(2026, 11, 10);
        model.Deadline = task.Deadline;

        var result = await model.OnPostAsync(task.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal("Разработка нового чертежа", saved.Title);
        Assert.Equal(engineer.Id, saved.AssigneeId);
        Assert.Equal(EngineeringTaskStatus.Assigned, saved.Status);
        Assert.Equal(EngineeringTaskPriority.Normal, saved.Priority);
        Assert.Equal(boat.Id, saved.BoatId);
        Assert.Null(saved.StartedOn);
        Assert.Equal(new DateOnly(2026, 11, 10), saved.PlannedDueOn);
        Assert.Null(saved.CompletedOn);
        Assert.Equal(new DateOnly(2026, 10, 15), saved.Deadline);
        Assert.Equal("К-100", saved.HullNumber);

        var audit = await _db.AuditLogs.SingleAsync();
        Assert.Equal("editor-user", audit.UserId);
        Assert.Equal("Редактор", audit.UserName);
        Assert.Equal("Updated", audit.Action);
        Assert.Equal("EngineeringTask", audit.EntityName);
        Assert.Equal(task.Id.ToString(), audit.EntityId);
        Assert.Contains("не назначен", audit.Details);
        Assert.Contains(engineer.DisplayName, audit.Details);

        var notification = await _db.Notifications.SingleAsync();
        Assert.Equal(engineer.Id, notification.UserId);
        Assert.Equal("Вам назначена задача КБ", notification.Title);
        Assert.Contains("Разработка нового чертежа", notification.Message);
        Assert.Contains("15.10.2026", notification.Message);
        Assert.Equal("/Engineering/Index", notification.Link);
    }

    [Theory]
    [InlineData(AppRoles.Engineer)]
    [InlineData(AppRoles.ProductionHead)]
    public async Task EngineerOrProductionHead_CannotEdit(string role)
    {
        var originalAssignee = await CreateUserAsync("Исходный инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(originalAssignee.Id);
        var model = CreateModel(role);
        SetValidInput(model, originalAssignee.Id);
        model.Title = "Недопустимое изменение";

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<ForbidResult>(result);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        AssertOriginalTask(saved, originalAssignee.Id);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(false, AppRoles.Engineer)]
    [InlineData(true, AppRoles.ProductionHead)]
    public async Task InactiveOrNonEngineerAssignee_IsRejectedWithoutChanges(bool isActive, string role)
    {
        var originalAssignee = await CreateUserAsync("Исходный инженер", AppRoles.Engineer);
        var rejectedAssignee = await CreateUserAsync("Неподходящий исполнитель", role, isActive);
        var task = await SeedTaskAsync(originalAssignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, rejectedAssignee.Id);
        model.Title = "Изменение не сохраняется";

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey(nameof(model.AssigneeId)));
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        AssertOriginalTask(saved, originalAssignee.Id);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(nameof(EditModel.Title), 1001, "Название задачи не может быть длиннее 1000 символов.")]
    [InlineData(nameof(EditModel.DeadlineChangeComment), 1001, "Комментарий к переносу дедлайна не может быть длиннее 1000 символов.")]
    public async Task TooLongTitleOrDeadlineChangeComment_IsRejectedWithoutChanges(
        string propertyName, int length, string expectedError)
    {
        var originalAssignee = await CreateUserAsync("Исходный инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(originalAssignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, originalAssignee.Id);
        if (propertyName == nameof(EditModel.Title))
            model.Title = new string('Н', length);
        else
            model.DeadlineChangeComment = new string('К', length);
        ValidateDataAnnotations(model);

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Equal(expectedError, Assert.Single(model.ModelState[propertyName]!.Errors).ErrorMessage);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        AssertOriginalTask(saved, originalAssignee.Id);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task StatusJumpOverOneStep_IsRejectedWithoutChanges()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.Status = EngineeringTaskStatus.Done;

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey(nameof(model.Status)));
        _db.ChangeTracker.Clear();
        AssertOriginalTask(await _db.EngineeringTasks.SingleAsync(), assignee.Id);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task DeadlineChangeWithoutComment_IsRejectedWithoutChanges()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.Deadline = new DateOnly(2026, 11, 15);
        model.DeadlineChangeComment = "   ";

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey(nameof(model.DeadlineChangeComment)));
        _db.ChangeTracker.Clear();
        AssertOriginalTask(await _db.EngineeringTasks.SingleAsync(), assignee.Id);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task InitialDeadlineWithoutComment_SavesDeadlineWritesAuditAndNotifiesAssignee()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        task.Deadline = null;
        await _db.SaveChangesAsync();
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.Deadline = new DateOnly(2026, 11, 15);
        model.DeadlineChangeComment = null;

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(result);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 15), saved.Deadline);
        Assert.Equal(2, await _db.AuditLogs.CountAsync());
        var deadlineAudit = await _db.AuditLogs.SingleAsync(a => a.Action == "DeadlineChanged");
        Assert.Contains("не задан → 15.11.2026", deadlineAudit.Details);
        Assert.Contains("комментарий: не указан", deadlineAudit.Details);
        var notification = await _db.Notifications.SingleAsync();
        Assert.Equal(assignee.Id, notification.UserId);
        Assert.Equal("Изменён дедлайн задачи КБ", notification.Title);
        Assert.Contains("не задан → 15.11.2026", notification.Message);
        Assert.Contains("Комментарий: не указан", notification.Message);
    }

    [Fact]
    public async Task DeadlineChange_ResetsRemindersWritesAuditAndNotifiesAssignee()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        task.DeadlineReminderSentOn = task.Deadline;
        task.OverdueReminderSentOn = task.Deadline;
        await _db.SaveChangesAsync();
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.Deadline = new DateOnly(2026, 11, 15);
        model.DeadlineChangeComment = "  Изменился план производства  ";

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(result);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 15), saved.Deadline);
        Assert.Null(saved.DeadlineReminderSentOn);
        Assert.Null(saved.OverdueReminderSentOn);
        Assert.Equal(2, await _db.AuditLogs.CountAsync());
        var deadlineAudit = await _db.AuditLogs.SingleAsync(a => a.Action == "DeadlineChanged");
        Assert.Contains("15.10.2026 → 15.11.2026", deadlineAudit.Details);
        Assert.Contains("Изменился план производства", deadlineAudit.Details);
        var notification = await _db.Notifications.SingleAsync();
        Assert.Equal(assignee.Id, notification.UserId);
        Assert.Equal("Изменён дедлайн задачи КБ", notification.Title);
        Assert.Contains("Изменился план производства", notification.Message);
    }

    [Fact]
    public async Task PriorityChange_NotifiesAssignee()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.Priority = EngineeringTaskPriority.Urgent;

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(result);
        _db.ChangeTracker.Clear();
        Assert.Equal(EngineeringTaskPriority.Urgent, (await _db.EngineeringTasks.SingleAsync()).Priority);
        var notification = await _db.Notifications.SingleAsync();
        Assert.Equal(assignee.Id, notification.UserId);
        Assert.Equal("Изменён приоритет задачи КБ", notification.Title);
        Assert.Contains("Обычный → Срочный", notification.Message);
    }

    [Fact]
    public async Task StatusTransitions_SetAndClearAutomaticDates()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var inProgressModel = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(inProgressModel, assignee.Id);
        inProgressModel.Status = EngineeringTaskStatus.InProgress;

        Assert.IsType<RedirectToPageResult>(await inProgressModel.OnPostAsync(task.Id));
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(EngineeringTaskStatus.InProgress, saved.Status);
        Assert.Equal(today, saved.StartedOn);
        Assert.Null(saved.CompletedOn);

        var doneModel = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(doneModel, assignee.Id);
        doneModel.Status = EngineeringTaskStatus.Done;
        Assert.IsType<RedirectToPageResult>(await doneModel.OnPostAsync(task.Id));
        _db.ChangeTracker.Clear();
        saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(EngineeringTaskStatus.Done, saved.Status);
        Assert.Equal(today, saved.StartedOn);
        Assert.Equal(today, saved.CompletedOn);

        var reopenedModel = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(reopenedModel, assignee.Id);
        reopenedModel.Status = EngineeringTaskStatus.InProgress;
        Assert.IsType<RedirectToPageResult>(await reopenedModel.OnPostAsync(task.Id));
        _db.ChangeTracker.Clear();
        saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(EngineeringTaskStatus.InProgress, saved.Status);
        Assert.Equal(today, saved.StartedOn);
        Assert.Null(saved.CompletedOn);

        var assignedModel = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(assignedModel, assignee.Id);
        Assert.IsType<RedirectToPageResult>(await assignedModel.OnPostAsync(task.Id));
        _db.ChangeTracker.Clear();
        saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(EngineeringTaskStatus.Assigned, saved.Status);
        Assert.Null(saved.StartedOn);
        Assert.Null(saved.CompletedOn);
    }

    [Fact]
    public async Task RemovingAssignee_ResetsStatusAndAutomaticDates()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        task.Status = EngineeringTaskStatus.InProgress;
        task.StartedOn = new DateOnly(2026, 10, 1);
        await _db.SaveChangesAsync();
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.AssigneeId = null;
        model.Status = EngineeringTaskStatus.InProgress;

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(result);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Null(saved.AssigneeId);
        Assert.Equal(EngineeringTaskStatus.New, saved.Status);
        Assert.Null(saved.StartedOn);
        Assert.Null(saved.CompletedOn);
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task RemovingAssigneeFromDone_ResetsStatusAndClearsAutomaticDates()
    {
        var assignee = await CreateUserAsync("Инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(assignee.Id);
        task.Status = EngineeringTaskStatus.Done;
        task.StartedOn = new DateOnly(2026, 10, 1);
        task.CompletedOn = new DateOnly(2026, 10, 20);
        await _db.SaveChangesAsync();
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, assignee.Id);
        model.AssigneeId = null;
        model.Status = EngineeringTaskStatus.Done;

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(result);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Null(saved.AssigneeId);
        Assert.Equal(EngineeringTaskStatus.New, saved.Status);
        Assert.Null(saved.StartedOn);
        Assert.Null(saved.CompletedOn);
        var audit = await _db.AuditLogs.SingleAsync();
        Assert.Equal("Updated", audit.Action);
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task SavingSameAssigneeAgain_DoesNotDuplicateNotification()
    {
        var engineer = await CreateUserAsync("Инженер Сидоров", AppRoles.Engineer);
        var task = await SeedTaskAsync();
        var firstModel = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(firstModel, engineer.Id);

        var firstResult = await firstModel.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(firstResult);
        Assert.Equal(1, await _db.Notifications.CountAsync());
        _db.ChangeTracker.Clear();
        var secondModel = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(secondModel, engineer.Id);

        var secondResult = await secondModel.OnPostAsync(task.Id);

        Assert.IsType<RedirectToPageResult>(secondResult);
        var notification = await _db.Notifications.SingleAsync();
        Assert.Equal(engineer.Id, notification.UserId);
        Assert.Equal(2, await _db.AuditLogs.CountAsync());
    }

    private async Task<ApplicationUser> CreateUserAsync(string displayName, string role, bool isActive = true)
    {
        if (!await _roleManager.RoleExistsAsync(role))
        {
            var roleResult = await _roleManager.CreateAsync(new IdentityRole(role));
            Assert.True(roleResult.Succeeded, IdentityErrors(roleResult));
        }

        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            IsActive = isActive,
        };
        var createResult = await _userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, IdentityErrors(createResult));
        var roleAssignmentResult = await _userManager.AddToRoleAsync(user, role);
        Assert.True(roleAssignmentResult.Succeeded, IdentityErrors(roleAssignmentResult));
        return user;
    }

    private async Task<EngineeringTask> SeedTaskAsync(string? assigneeId = null)
    {
        var task = new EngineeringTask
        {
            CreatedByUserId = "creator-user",
            CreatedByName = "Главный инженер",
            Title = "Исходная задача",
            Priority = EngineeringTaskPriority.Normal,
            Status = assigneeId is null ? EngineeringTaskStatus.New : EngineeringTaskStatus.Assigned,
            AssigneeId = assigneeId,
            PlannedDueOn = new DateOnly(2026, 10, 10),
            Deadline = new DateOnly(2026, 10, 15),
            HullNumber = "К-100",
        };
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    private EditModel CreateModel(string role)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "editor-user"),
                new Claim(ClaimTypes.Name, "Редактор"),
                new Claim(ClaimTypes.Role, role),
            }, "EngineeringTaskEditTests")),
        };
        var currentUser = new CurrentUserService(
            new HttpContextAccessor { HttpContext = httpContext }, _db);
        var model = new EditModel(_db, _userManager, currentUser,
            new EngineeringTaskService(_db, _userManager, new NotificationService(_db), new AuditService(_db)));
        model.TempData = new TempDataDictionary(httpContext, new EngineeringTaskEditTempDataProvider());
        return model;
    }

    private static void ValidateDataAnnotations(EditModel model)
    {
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);
        foreach (var validationResult in validationResults)
        foreach (var memberName in validationResult.MemberNames)
            model.ModelState.AddModelError(memberName, validationResult.ErrorMessage ?? string.Empty);
    }

    private static void SetValidInput(EditModel model, string assigneeId)
    {
        model.Title = "Повторное сохранение";
        model.Priority = EngineeringTaskPriority.Normal;
        model.Status = EngineeringTaskStatus.Assigned;
        model.AssigneeId = assigneeId;
        model.BoatId = null;
        model.PlannedDueOn = new DateOnly(2026, 11, 10);
        model.Deadline = new DateOnly(2026, 10, 15);
    }

    private static void AssertOriginalTask(EngineeringTask saved, string assigneeId)
    {
        Assert.Equal("Исходная задача", saved.Title);
        Assert.Equal(assigneeId, saved.AssigneeId);
        Assert.Equal(EngineeringTaskStatus.Assigned, saved.Status);
        Assert.Equal(EngineeringTaskPriority.Normal, saved.Priority);
        Assert.Null(saved.BoatId);
        Assert.Null(saved.StartedOn);
        Assert.Equal(new DateOnly(2026, 10, 10), saved.PlannedDueOn);
        Assert.Null(saved.CompletedOn);
        Assert.Equal(new DateOnly(2026, 10, 15), saved.Deadline);
        Assert.Equal("К-100", saved.HullNumber);
    }

    private static string IdentityErrors(IdentityResult result)
        => string.Join("; ", result.Errors.Select(error => error.Description));

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private sealed class EngineeringTaskEditTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
            => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
