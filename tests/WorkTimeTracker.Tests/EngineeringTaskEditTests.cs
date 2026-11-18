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
    public async Task ChiefEngineerOrAdmin_AssignsActiveEngineerAndSavesAllFields(string role)
    {
        var engineer = await CreateUserAsync("Инженер Петров", AppRoles.Engineer);
        var task = await SeedTaskAsync();
        var model = CreateModel(role);
        model.Title = "  Разработка нового чертежа  ";
        model.AssigneeId = engineer.Id;
        model.StartedOn = new DateOnly(2026, 11, 1);
        model.PlannedDueOn = new DateOnly(2026, 11, 10);
        model.CompletedOn = new DateOnly(2026, 11, 12);
        model.Deadline = new DateOnly(2026, 11, 15);
        model.HullNumber = "  К-204  ";

        var result = await model.OnPostAsync(task.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal("Разработка нового чертежа", saved.Title);
        Assert.Equal(engineer.Id, saved.AssigneeId);
        Assert.Equal(new DateOnly(2026, 11, 1), saved.StartedOn);
        Assert.Equal(new DateOnly(2026, 11, 10), saved.PlannedDueOn);
        Assert.Equal(new DateOnly(2026, 11, 12), saved.CompletedOn);
        Assert.Equal(new DateOnly(2026, 11, 15), saved.Deadline);
        Assert.Equal("К-204", saved.HullNumber);

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
        Assert.Contains("15.11.2026", notification.Message);
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
        model.Title = "Недопустимое изменение";
        model.AssigneeId = null;
        model.StartedOn = new DateOnly(2026, 12, 1);
        model.PlannedDueOn = new DateOnly(2026, 12, 2);
        model.CompletedOn = new DateOnly(2026, 12, 3);
        model.Deadline = new DateOnly(2026, 12, 4);
        model.HullNumber = "К-999";

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
        model.Title = "Изменение не сохраняется";
        model.AssigneeId = rejectedAssignee.Id;
        model.StartedOn = new DateOnly(2026, 12, 1);
        model.PlannedDueOn = new DateOnly(2026, 12, 2);
        model.CompletedOn = new DateOnly(2026, 12, 3);
        model.Deadline = new DateOnly(2026, 12, 4);
        model.HullNumber = "К-999";

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
    [InlineData(nameof(EditModel.HullNumber), 101, "Номер корпуса не может быть длиннее 100 символов.")]
    public async Task TooLongTitleOrHullNumber_IsRejectedWithoutChanges(
        string propertyName, int length, string expectedError)
    {
        var originalAssignee = await CreateUserAsync("Исходный инженер", AppRoles.Engineer);
        var newAssignee = await CreateUserAsync("Новый инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(originalAssignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, newAssignee.Id);
        if (propertyName == nameof(EditModel.Title))
            model.Title = new string('Н', length);
        else
            model.HullNumber = new string('К', length);
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
    public async Task CompletedOnBeforeStartedOn_IsRejectedWithoutChanges()
    {
        var originalAssignee = await CreateUserAsync("Исходный инженер", AppRoles.Engineer);
        var newAssignee = await CreateUserAsync("Новый инженер", AppRoles.Engineer);
        var task = await SeedTaskAsync(originalAssignee.Id);
        var model = CreateModel(AppRoles.ChiefEngineer);
        SetValidInput(model, newAssignee.Id);
        model.StartedOn = new DateOnly(2026, 11, 20);
        model.CompletedOn = new DateOnly(2026, 11, 19);

        var result = await model.OnPostAsync(task.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Equal("Дата завершения не может быть раньше даты начала.",
            Assert.Single(model.ModelState[nameof(model.CompletedOn)]!.Errors).ErrorMessage);
        _db.ChangeTracker.Clear();
        var saved = await _db.EngineeringTasks.SingleAsync();
        AssertOriginalTask(saved, originalAssignee.Id);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
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
            AssigneeId = assigneeId,
            StartedOn = new DateOnly(2026, 10, 1),
            PlannedDueOn = new DateOnly(2026, 10, 10),
            CompletedOn = new DateOnly(2026, 10, 12),
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
            new NotificationService(_db), new AuditService(_db));
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
        model.AssigneeId = assigneeId;
        model.StartedOn = new DateOnly(2026, 11, 1);
        model.PlannedDueOn = new DateOnly(2026, 11, 10);
        model.CompletedOn = new DateOnly(2026, 11, 12);
        model.Deadline = new DateOnly(2026, 11, 15);
        model.HullNumber = "К-204";
    }

    private static void AssertOriginalTask(EngineeringTask saved, string assigneeId)
    {
        Assert.Equal("Исходная задача", saved.Title);
        Assert.Equal(assigneeId, saved.AssigneeId);
        Assert.Equal(new DateOnly(2026, 10, 1), saved.StartedOn);
        Assert.Equal(new DateOnly(2026, 10, 10), saved.PlannedDueOn);
        Assert.Equal(new DateOnly(2026, 10, 12), saved.CompletedOn);
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
