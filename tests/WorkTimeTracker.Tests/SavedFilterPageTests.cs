using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Pages.Admin;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class SavedFilterPageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _services;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DefaultHttpContext _httpContext;
    private readonly CurrentUserService _currentUser;
    private readonly SavedFilterService _savedFilters;

    public SavedFilterPageTests()
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
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _services = services.BuildServiceProvider();
        _userManager = _services.GetRequiredService<UserManager<ApplicationUser>>();

        _httpContext = new DefaultHttpContext();
        _currentUser = new CurrentUserService(new HttpContextAccessor { HttpContext = _httpContext }, _db);
        _savedFilters = new SavedFilterService(_db, _currentUser);
    }

    [Fact]
    public async Task Master_SavesRestoresAndResetsPeriod()
    {
        SignIn("master", AppRoles.SectionMaster);
        var department = new Department { Name = "Участок" };
        _db.Employees.Add(new Employee
        {
            UserId = "master",
            FullName = "Мастер",
            Position = "Мастер",
            Department = department,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        Query("?from=2026-11-02&to=2026-11-08");
        var explicitModel = MasterModel();
        await explicitModel.OnGet(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));
        Assert.Equal(new DateOnly(2026, 11, 2), explicitModel.From);
        Assert.Equal(new DateOnly(2026, 11, 8), explicitModel.To);

        Query(null);
        var restoredModel = MasterModel();
        await restoredModel.OnGet(null, null);
        Assert.True(restoredModel.FiltersRestored);
        Assert.Equal(new DateOnly(2026, 11, 2), restoredModel.From);
        Assert.Equal(new DateOnly(2026, 11, 8), restoredModel.To);

        Query("?reset=1");
        await MasterModel().OnGet(null, null);
        Assert.Empty(await _savedFilters.GetAsync(SavedFilterService.MasterPage));
    }

    [Fact]
    public async Task Engineering_SavesRestoresAndResetsStatusPriorityAndSearch()
    {
        SignIn("engineer", AppRoles.Engineer);
        Query("?filter=all&priority=2&search=Корпус");
        await EngineeringModel().OnGetAsync("all", EngineeringTaskPriority.High, " Корпус ");

        Query(null);
        var restoredModel = EngineeringModel();
        await restoredModel.OnGetAsync(null, null, null);
        Assert.True(restoredModel.FiltersRestored);
        Assert.Equal("all", restoredModel.Filter);
        Assert.Equal(EngineeringTaskPriority.High, restoredModel.PriorityFilter);
        Assert.Equal("Корпус", restoredModel.Search);

        Query("?reset=1");
        var resetModel = EngineeringModel();
        await resetModel.OnGetAsync(null, null, null);
        Assert.Equal("active", resetModel.Filter);
        Assert.Null(resetModel.PriorityFilter);
        Assert.Null(resetModel.Search);
        Assert.Empty(await _savedFilters.GetAsync(SavedFilterService.EngineeringPage));
    }

    [Fact]
    public async Task Audit_SavesRestoresAndResetsCurrentFilters()
    {
        SignIn("admin", AppRoles.Admin);
        Query("?user=ivanov&entity=WorkDay");
        await AuditModel().OnGet(user: "ivanov", entity: "WorkDay");

        Query(null);
        var restoredModel = AuditModel();
        await restoredModel.OnGet();
        Assert.True(restoredModel.FiltersRestored);
        Assert.Equal("ivanov", restoredModel.FilterUser);
        Assert.Equal("WorkDay", restoredModel.FilterEntity);

        Query("?reset=1");
        var resetModel = AuditModel();
        await resetModel.OnGet();
        Assert.Null(resetModel.FilterUser);
        Assert.Null(resetModel.FilterEntity);
        Assert.Empty(await _savedFilters.GetAsync(SavedFilterService.AuditPage));
    }

    [Fact]
    public async Task Reports_ExportUpdatesPeriodAndCleanEntryRestoresAllFilters()
    {
        SignIn("admin", AppRoles.Admin);
        Query("?employeeId=12&year=2025&month=9&from=2025-09-01&to=2025-09-30");
        await ReportsModel().OnGet(12, 2025, 9, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), null);

        var exportModel = ReportsModel();
        var result = await exportModel.OnGetBoatsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null);
        Assert.IsType<FileContentResult>(result);

        Query(null);
        var restoredModel = ReportsModel();
        await restoredModel.OnGet(null, null, null, null, null, null);
        Assert.True(restoredModel.FiltersRestored);
        Assert.Equal(12, restoredModel.EmployeeId);
        Assert.Equal(2025, restoredModel.Year);
        Assert.Equal(9, restoredModel.Month);
        Assert.Equal(new DateOnly(2026, 11, 1), restoredModel.From);
        Assert.Equal(new DateOnly(2026, 11, 30), restoredModel.To);
        Assert.Null(restoredModel.DepartmentId);

        Query("?reset=1");
        await ReportsModel().OnGet(null, null, null, null, null, null);
        Assert.Empty(await _savedFilters.GetAsync(SavedFilterService.ReportsPage));
    }

    private WorkTimeTracker.Pages.Master.IndexModel MasterModel()
    {
        var model = new WorkTimeTracker.Pages.Master.IndexModel(
            new ReportService(_db), _currentUser, _savedFilters);
        UseHttpContext(model);
        return model;
    }

    private WorkTimeTracker.Pages.Engineering.IndexModel EngineeringModel()
    {
        var tasks = new EngineeringTaskService(_db, _userManager, new NotificationService(_db), new AuditService(_db));
        var model = new WorkTimeTracker.Pages.Engineering.IndexModel(
            _db, _userManager, _currentUser, tasks, _savedFilters);
        UseHttpContext(model);
        return model;
    }

    private AuditModel AuditModel()
    {
        var model = new AuditModel(_db, _savedFilters);
        UseHttpContext(model);
        return model;
    }

    private ReportsModel ReportsModel()
    {
        var model = new ReportsModel(_db, new ReportService(_db), new ExcelService(), _savedFilters);
        UseHttpContext(model);
        return model;
    }

    private void SignIn(string userId, string role)
    {
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
        ], "Test"));
    }

    private void Query(string? query) => _httpContext.Request.QueryString =
        query is null ? QueryString.Empty : new QueryString(query);

    private void UseHttpContext(PageModel model) => model.PageContext = new PageContext { HttpContext = _httpContext };

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }
}
