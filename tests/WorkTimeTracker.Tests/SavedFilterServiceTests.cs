using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class SavedFilterServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly DefaultHttpContext _httpContext;
    private readonly SavedFilterService _service;

    public SavedFilterServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _httpContext = new DefaultHttpContext();
        var currentUser = new CurrentUserService(new HttpContextAccessor { HttpContext = _httpContext }, _db);
        _service = new SavedFilterService(_db, currentUser);
    }

    [Fact]
    public async Task SaveAsync_StoresOnlyWhitelistedKeysAndReadsThemBack()
    {
        SignIn("user-a");

        var saved = await _service.SaveAsync(SavedFilterService.EngineeringPage, new Dictionary<string, string?>
        {
            ["filter"] = "completed",
            ["priority"] = "2",
            ["search"] = "Чертёж",
            ["isAdmin"] = "true",
        });

        Assert.True(saved);
        var filters = await _service.GetAsync(SavedFilterService.EngineeringPage);
        Assert.Equal("completed", filters["filter"]);
        Assert.Equal("2", filters["priority"]);
        Assert.Equal("Чертёж", filters["search"]);
        Assert.False(filters.ContainsKey("isAdmin"));
        var setting = Assert.Single(_db.SystemSettings);
        Assert.Contains("user-a", setting.Key);
        Assert.DoesNotContain("isAdmin", setting.Value);
    }

    [Fact]
    public async Task GetSaveAndReset_AreIsolatedByOwner()
    {
        SignIn("user-a");
        await _service.SaveAsync(SavedFilterService.MasterPage, new Dictionary<string, string?>
        {
            ["from"] = "2026-11-01",
            ["to"] = "2026-11-30",
        });

        SignIn("user-b");
        Assert.Empty(await _service.GetAsync(SavedFilterService.MasterPage));
        await _service.SaveAsync(SavedFilterService.MasterPage, new Dictionary<string, string?>
        {
            ["from"] = "2026-10-01",
            ["to"] = "2026-10-31",
        });
        await _service.ResetAsync(SavedFilterService.MasterPage);
        Assert.Empty(await _service.GetAsync(SavedFilterService.MasterPage));

        SignIn("user-a");
        var filters = await _service.GetAsync(SavedFilterService.MasterPage);
        Assert.Equal("2026-11-01", filters["from"]);
        Assert.Equal("2026-11-30", filters["to"]);
        Assert.Single(_db.SystemSettings);
    }

    [Fact]
    public async Task SaveAsync_MergesPartialPageFiltersAndClearsSuppliedValues()
    {
        SignIn("user-a");
        await _service.SaveAsync(SavedFilterService.ReportsPage, new Dictionary<string, string?>
        {
            ["employeeId"] = "12",
            ["year"] = "2026",
            ["month"] = "10",
            ["from"] = "2026-10-01",
            ["to"] = "2026-10-31",
            ["departmentId"] = "3",
        });

        await _service.SaveAsync(SavedFilterService.ReportsPage, new Dictionary<string, string?>
        {
            ["from"] = "2026-11-01",
            ["to"] = "2026-11-30",
            ["departmentId"] = null,
        });

        var filters = await _service.GetAsync(SavedFilterService.ReportsPage);
        Assert.Equal("12", filters["employeeId"]);
        Assert.Equal("2026-11-01", filters["from"]);
        Assert.Equal("2026-11-30", filters["to"]);
        Assert.False(filters.ContainsKey("departmentId"));
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedJsonWithoutReplacingPreviousValue()
    {
        SignIn("user-a");
        await _service.SaveAsync(SavedFilterService.EngineeringPage, new Dictionary<string, string?>
        {
            ["filter"] = "active",
            ["search"] = "Сохранено",
        });

        var saved = await _service.SaveAsync(SavedFilterService.EngineeringPage, new Dictionary<string, string?>
        {
            ["search"] = new string('я', 5000),
        });

        Assert.False(saved);
        var filters = await _service.GetAsync(SavedFilterService.EngineeringPage);
        Assert.Equal("Сохранено", filters["search"]);
    }

    [Fact]
    public async Task GetAsync_IgnoresCorruptOrOversizedStoredJson()
    {
        SignIn("user-a");
        await _service.SaveAsync(SavedFilterService.AuditPage, new Dictionary<string, string?>
        {
            ["user"] = "admin",
        });
        var setting = Assert.Single(_db.SystemSettings);
        setting.Value = "{broken";
        await _db.SaveChangesAsync();

        Assert.Empty(await _service.GetAsync(SavedFilterService.AuditPage));

        setting.Value = new string('x', 5000);
        await _db.SaveChangesAsync();
        Assert.Empty(await _service.GetAsync(SavedFilterService.AuditPage));
    }

    [Fact]
    public async Task AnonymousUser_CannotReadOrWriteSavedFilters()
    {
        Assert.False(await _service.SaveAsync(SavedFilterService.AuditPage, new Dictionary<string, string?>
        {
            ["user"] = "admin",
        }));
        Assert.Empty(await _service.GetAsync(SavedFilterService.AuditPage));
        Assert.Empty(_db.SystemSettings);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.GetAsync("unknown"));
    }

    private void SignIn(string userId)
    {
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
