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

public class EngineeringTaskCreateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _services;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EngineeringTaskCreateTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_db);
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _services = services.BuildServiceProvider();
        _userManager = _services.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _services.GetRequiredService<RoleManager<IdentityRole>>();
    }

    [Fact]
    public async Task ProductionHead_CreateTask_SavesAuthorBoatPriorityAuditAndNotifiesActiveChiefEngineers()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead, "Начальник производства");
        var firstChief = await CreateUserAsync("chief-one", AppRoles.ChiefEngineer);
        var secondChief = await CreateUserAsync("chief-two", AppRoles.ChiefEngineer);
        var boat = new Boat { Number = "К-123", Name = "Катер" };
        _db.Boats.Add(boat);
        await _db.SaveChangesAsync();
        var model = CreateModel(creator, AppRoles.ProductionHead);
        model.Title = "  Разработать чертёж  ";
        model.BoatId = boat.Id;
        model.Priority = EngineeringTaskPriority.Urgent;
        model.Deadline = new DateOnly(2026, 12, 15);

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<RedirectToPageResult>(result);
        var task = Assert.Single(_db.EngineeringTasks);
        Assert.Equal(creator.Id, task.CreatedByUserId);
        Assert.Equal("Начальник производства", task.CreatedByName);
        Assert.Equal("Разработать чертёж", task.Title);
        Assert.Equal(boat.Id, task.BoatId);
        Assert.Equal(EngineeringTaskPriority.Urgent, task.Priority);
        Assert.Equal(new DateOnly(2026, 12, 15), task.Deadline);
        Assert.Equal(EngineeringTaskStatus.New, task.Status);

        var audit = Assert.Single(_db.AuditLogs);
        Assert.Equal(creator.Id, audit.UserId);
        Assert.Equal("Created", audit.Action);
        Assert.Equal("EngineeringTask", audit.EntityName);
        Assert.Equal(task.Id.ToString(), audit.EntityId);

        Assert.Equal(2, _db.Notifications.Count());
        Assert.Contains(_db.Notifications, n => n.UserId == firstChief.Id
            && n.Title == "Новое задание КБ"
            && n.Link == $"/Engineering/Edit/{task.Id}");
        Assert.Contains(_db.Notifications, n => n.UserId == secondChief.Id);
    }

    [Fact]
    public async Task ProductionHead_CreateTaskWithEmailNameClaimAndWithoutDisplayNameClaim_UsesIdentityDisplayName()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead, "Иванов Иван Иванович");
        var chief = await CreateUserAsync("chief", AppRoles.ChiefEngineer);
        var model = CreateModel(creator, AppRoles.ProductionHead,
            nameClaim: creator.Email, includeDisplayNameClaim: false);
        model.Title = "Задание с автором из Identity";

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<RedirectToPageResult>(result);
        var task = Assert.Single(_db.EngineeringTasks);
        Assert.Equal("Иванов Иван Иванович", task.CreatedByName);
        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(chief.Id, notification.UserId);
        Assert.Equal("Иванов Иван Иванович: Задание с автором из Identity", notification.Message);
    }

    [Fact]
    public async Task ChiefEngineer_CreateTask_SavesTask()
    {
        var creator = await CreateUserAsync("chief-creator", AppRoles.ChiefEngineer);
        var model = CreateModel(creator, AppRoles.ChiefEngineer);
        model.Title = "Задание от главного инженера";

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<RedirectToPageResult>(result);
        var task = Assert.Single(_db.EngineeringTasks);
        Assert.Equal(creator.Id, task.CreatedByUserId);
        Assert.Equal("Задание от главного инженера", task.Title);
        Assert.Equal(EngineeringTaskStatus.New, task.Status);
    }

    [Fact]
    public async Task Admin_CreateTask_SavesTask()
    {
        var creator = await CreateUserAsync("admin", AppRoles.Admin);
        var model = CreateModel(creator, AppRoles.Admin);
        model.Title = "Задание от администратора";

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<RedirectToPageResult>(result);
        var task = Assert.Single(_db.EngineeringTasks);
        Assert.Equal(creator.Id, task.CreatedByUserId);
        Assert.Equal("Задание от администратора", task.Title);
        Assert.Equal(EngineeringTaskPriority.Normal, task.Priority);
        Assert.Equal(EngineeringTaskStatus.New, task.Status);
    }

    [Fact]
    public async Task Engineer_CreateTask_ReturnsForbid()
    {
        var creator = await CreateUserAsync("forbidden-user", AppRoles.Engineer);
        var model = CreateModel(creator, AppRoles.Engineer);
        model.Title = "Недоступное задание";

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(_db.EngineeringTasks);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task ProductionHead_CreateTaskWithEmptyTitle_DoesNotSaveTask()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead);
        var model = CreateModel(creator, AppRoles.ProductionHead);
        model.Title = "   ";

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Empty(_db.EngineeringTasks);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task ProductionHead_CreateTaskWithTooLongTitle_DoesNotSaveTask()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead);
        var model = CreateModel(creator, AppRoles.ProductionHead);
        model.Title = new string('З', 1001);
        AddValidationErrorsToModelState(model);

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Empty(_db.EngineeringTasks);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task ProductionHead_CreateTaskWithInactiveBoat_DoesNotSaveTask()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead);
        var boat = new Boat { Number = "К-123", IsActive = false };
        _db.Boats.Add(boat);
        await _db.SaveChangesAsync();
        var model = CreateModel(creator, AppRoles.ProductionHead);
        model.Title = "Корректное задание";
        model.BoatId = boat.Id;

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey(nameof(model.BoatId)));
        Assert.Empty(_db.EngineeringTasks);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task ProductionHead_CreateTaskWithInvalidPriority_DoesNotSaveTask()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead);
        var model = CreateModel(creator, AppRoles.ProductionHead);
        model.Title = "Корректное задание";
        model.Priority = (EngineeringTaskPriority)99;

        var result = await model.OnPostCreateAsync(null, null, null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey(nameof(model.Priority)));
        Assert.Empty(_db.EngineeringTasks);
        Assert.Empty(_db.AuditLogs);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task ProductionHead_CreateTask_DoesNotNotifyInactiveChiefEngineer()
    {
        var creator = await CreateUserAsync("production-head", AppRoles.ProductionHead);
        var activeChief = await CreateUserAsync("active-chief", AppRoles.ChiefEngineer);
        var inactiveChief = await CreateUserAsync("inactive-chief", AppRoles.ChiefEngineer, isActive: false);
        var model = CreateModel(creator, AppRoles.ProductionHead);
        model.Title = "Задание для главных инженеров";

        await model.OnPostCreateAsync(null, null, null);

        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(activeChief.Id, notification.UserId);
        Assert.DoesNotContain(_db.Notifications, n => n.UserId == inactiveChief.Id);
    }

    private async Task<ApplicationUser> CreateUserAsync(string userName, string role,
        string? displayName = null, bool isActive = true)
    {
        if (!await _roleManager.RoleExistsAsync(role))
            AssertSucceeded(await _roleManager.CreateAsync(new IdentityRole(role)));

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            DisplayName = displayName ?? userName,
            IsActive = isActive,
        };
        AssertSucceeded(await _userManager.CreateAsync(user));
        AssertSucceeded(await _userManager.AddToRoleAsync(user, role));
        return user;
    }

    private IndexModel CreateModel(ApplicationUser user, string role,
        string? nameClaim = null, bool includeDisplayNameClaim = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, nameClaim ?? user.UserName!),
            new(ClaimTypes.Role, role),
        };
        if (includeDisplayNameClaim)
            claims.Add(new Claim("DisplayName", user.DisplayName));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };
        var currentUser = new CurrentUserService(new HttpContextAccessor { HttpContext = httpContext }, _db);
        var model = new IndexModel(_db, _userManager, currentUser,
            new EngineeringTaskService(_db, _userManager, new NotificationService(_db), new AuditService(_db)));
        model.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return model;
    }

    private static void AddValidationErrorsToModelState(IndexModel model)
    {
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);
        foreach (var validationResult in validationResults)
        {
            foreach (var memberName in validationResult.MemberNames)
                model.ModelState.AddModelError(memberName, validationResult.ErrorMessage!);
        }
    }

    private static void AssertSucceeded(IdentityResult result)
        => Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
