using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class BulkTimeSheetApprovalServiceTests : IDisposable
{
    private static readonly DateOnly From = new(2026, 11, 2);
    private static readonly DateOnly To = new(2026, 11, 8);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public BulkTimeSheetApprovalServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = CreateContext(_connection);
        _db.Database.EnsureCreated();
    }

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.ProductionHead)]
    public async Task Approve_SelectedIds_ReturnsPerDayResultsWithoutDuplicates(string role)
    {
        var employee = Employee("Иванов Иван", "employee-user");
        var submitted = Day(employee, TimeSheetStatus.Submitted, From.AddDays(1));
        var draft = Day(employee, TimeSheetStatus.Draft, From.AddDays(2));
        var rejected = Day(employee, TimeSheetStatus.Rejected, From.AddDays(3));
        var approved = Day(employee, TimeSheetStatus.Approved, From.AddDays(4));
        var outside = Day(employee, TimeSheetStatus.Submitted, To.AddDays(1));
        _db.AddRange(employee, submitted, draft, rejected, approved, outside);
        await _db.SaveChangesAsync();

        var results = await Service(_db, role).ApproveAsync(new[]
        {
            submitted.Id, draft.Id, rejected.Id, approved.Id, outside.Id, int.MaxValue, submitted.Id,
        }, From, To);

        Assert.Equal(6, results.Count);
        Assert.Equal(new[]
        {
            BulkTimeSheetApprovalStatus.Approved,
            BulkTimeSheetApprovalStatus.AlreadyProcessed,
            BulkTimeSheetApprovalStatus.AlreadyProcessed,
            BulkTimeSheetApprovalStatus.AlreadyProcessed,
            BulkTimeSheetApprovalStatus.NotFound,
            BulkTimeSheetApprovalStatus.NotFound,
        }, results.Select(r => r.Status));
        _db.ChangeTracker.Clear();
        Assert.Equal(TimeSheetStatus.Approved, await _db.WorkDays.Where(w => w.Id == submitted.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(TimeSheetStatus.Draft, await _db.WorkDays.Where(w => w.Id == draft.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(TimeSheetStatus.Rejected, await _db.WorkDays.Where(w => w.Id == rejected.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(TimeSheetStatus.Approved, await _db.WorkDays.Where(w => w.Id == approved.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(TimeSheetStatus.Submitted, await _db.WorkDays.Where(w => w.Id == outside.Id).Select(w => w.Status).SingleAsync());
        var approvedDay = await _db.WorkDays.AsNoTracking().SingleAsync(w => w.Id == submitted.Id);
        Assert.Equal("admin-user", approvedDay.ApprovedByUserId);
        Assert.Equal("Администратор", approvedDay.ApprovedByName);
        Assert.Single(_db.Notifications);
        Assert.Single(_db.AuditLogs, log => log.Action == "Approved" && log.EntityId == submitted.Id.ToString());
        Assert.Equal("Утверждён", results[0].StatusText);
        Assert.Contains(employee.FullName, results[0].DisplayText);
    }

    [Fact]
    public async Task ApproveAll_ProcessesOnlySubmittedDaysInsideRange()
    {
        var employee = Employee("Петров Пётр", "employee-user");
        var first = Day(employee, TimeSheetStatus.Submitted, From);
        var last = Day(employee, TimeSheetStatus.Submitted, To);
        var approved = Day(employee, TimeSheetStatus.Approved, From.AddDays(1));
        var rejected = Day(employee, TimeSheetStatus.Rejected, From.AddDays(2));
        var outside = Day(employee, TimeSheetStatus.Submitted, To.AddDays(1));
        _db.AddRange(employee, first, last, approved, rejected, outside);
        await _db.SaveChangesAsync();

        var results = await Service(_db, AppRoles.ProductionHead).ApproveAllAsync(From, To);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(BulkTimeSheetApprovalStatus.Approved, r.Status));
        _db.ChangeTracker.Clear();
        Assert.Equal(2, await _db.WorkDays.CountAsync(w => w.Status == TimeSheetStatus.Approved
            && (w.Id == first.Id || w.Id == last.Id)));
        Assert.Equal(TimeSheetStatus.Rejected, await _db.WorkDays.Where(w => w.Id == rejected.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(TimeSheetStatus.Submitted, await _db.WorkDays.Where(w => w.Id == outside.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(2, await _db.Notifications.CountAsync());
        Assert.Equal(2, await _db.AuditLogs.CountAsync(log => log.Action == "Approved"));
    }

    [Fact]
    public async Task Approve_UserWithoutProductionAccess_ReturnsErrorWithoutSideEffects()
    {
        var employee = Employee("Сидоров Сидор", "employee-user");
        var submitted = Day(employee, TimeSheetStatus.Submitted, From.AddDays(1));
        _db.AddRange(employee, submitted);
        await _db.SaveChangesAsync();

        var result = Assert.Single(await Service(_db, AppRoles.SectionMaster)
            .ApproveAsync(new[] { submitted.Id }, From, To));

        Assert.Equal(BulkTimeSheetApprovalStatus.Error, result.Status);
        Assert.Equal("Ошибка", result.StatusText);
        _db.ChangeTracker.Clear();
        Assert.Equal(TimeSheetStatus.Submitted, await _db.WorkDays.Select(w => w.Status).SingleAsync());
        Assert.Empty(_db.Notifications);
        Assert.Empty(_db.AuditLogs);
    }

    [Fact]
    public async Task Approve_WhenOneDayFails_ContinuesAndPreservesPartialResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new ThrowingWorkDaySaveInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var employee = Employee("Работник", "employee-user");
        var failing = Day(employee, TimeSheetStatus.Submitted, From.AddDays(1));
        var succeeding = Day(employee, TimeSheetStatus.Submitted, From.AddDays(2));
        db.AddRange(employee, failing, succeeding);
        await db.SaveChangesAsync();
        interceptor.FailingDayId = failing.Id;

        var results = await Service(db, AppRoles.Admin).ApproveAsync(new[] { failing.Id, succeeding.Id }, From, To);

        Assert.Equal(BulkTimeSheetApprovalStatus.Error, results[0].Status);
        Assert.Equal(BulkTimeSheetApprovalStatus.Approved, results[1].Status);
        db.ChangeTracker.Clear();
        Assert.Equal(TimeSheetStatus.Submitted, await db.WorkDays.Where(w => w.Id == failing.Id).Select(w => w.Status).SingleAsync());
        Assert.Equal(TimeSheetStatus.Approved, await db.WorkDays.Where(w => w.Id == succeeding.Id).Select(w => w.Status).SingleAsync());
        Assert.Single(db.Notifications);
        Assert.Single(db.AuditLogs, log => log.EntityId == succeeding.Id.ToString());
    }

    [Fact]
    public async Task ProductionBulkApprove_RedirectsWithRangeAndResultPanel()
    {
        var employee = Employee("Иванов Иван", "employee-user");
        var submitted = Day(employee, TimeSheetStatus.Submitted, From.AddDays(1));
        _db.AddRange(employee, submitted);
        await _db.SaveChangesAsync();
        var currentUser = CurrentUser(_db, AppRoles.Admin);
        var audit = new AuditService(_db);
        var notifications = new NotificationService(_db);
        var model = new Pages.Production.IndexModel(_db, audit, currentUser, notifications,
            new BulkTimeSheetApprovalService(_db, audit, currentUser, notifications));
        model.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());

        var result = await model.OnPostBulkApproveAsync(new[] { submitted.Id }, From, To);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(From.ToString("yyyy-MM-dd"), redirect.RouteValues?["from"]);
        Assert.Equal(To.ToString("yyyy-MM-dd"), redirect.RouteValues?["to"]);
        Assert.Equal("Обработано: 1. Утверждено: 1.", model.TempData["BulkApprovalSummary"]);
        Assert.Contains("Утверждён", model.TempData["BulkApprovalResults"] as string);
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static Employee Employee(string fullName, string? userId = null)
        => new() { FullName = fullName, UserId = userId, Department = new Department { Name = "Сборка" } };

    private static WorkDay Day(Employee employee, TimeSheetStatus status, DateOnly date)
        => new() { Employee = employee, Status = status, Date = date };

    private static BulkTimeSheetApprovalService Service(ApplicationDbContext db, string role)
    {
        var currentUser = CurrentUser(db, role);
        return new BulkTimeSheetApprovalService(db, new AuditService(db), currentUser, new NotificationService(db));
    }

    private static CurrentUserService CurrentUser(ApplicationDbContext db, string role)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin-user"),
                new Claim(ClaimTypes.Name, "Администратор"),
                new Claim(ClaimTypes.Role, role),
            }, "Test")),
        };
        return new CurrentUserService(new HttpContextAccessor { HttpContext = httpContext }, db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class ThrowingWorkDaySaveInterceptor : SaveChangesInterceptor
    {
        public int FailingDayId { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<WorkDay>()
                .Any(e => e.Entity.Id == FailingDayId && e.State == EntityState.Modified) == true)
                throw new InvalidOperationException("Test failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
