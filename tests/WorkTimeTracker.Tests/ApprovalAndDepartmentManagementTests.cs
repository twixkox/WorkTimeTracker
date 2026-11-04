using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Pages.Admin;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class ApprovalAndDepartmentManagementTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public ApprovalAndDepartmentManagementTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.ProductionHead)]
    public async Task ProductionOrAdmin_ApproveSubmittedDay(string role)
    {
        var department = new Department { Name = "Сборка" };
        var employee = new Employee { FullName = "Работник", Department = department, UserId = "employee-user" };
        var date = new DateOnly(2026, 11, 4);
        var day = new WorkDay { Employee = employee, Date = date, Status = TimeSheetStatus.Submitted };
        _db.WorkDays.Add(day);
        await _db.SaveChangesAsync();

        var currentUser = CurrentUser(role);
        var model = new Pages.Production.IndexModel(_db, new ReportService(_db), new SettingsService(_db),
            new AuditService(_db), currentUser, new NotificationService(_db));
        SetTempData(model);

        await model.OnPostApproveAsync(day.Id, date, date);

        Assert.Equal(TimeSheetStatus.Approved, day.Status);
        Assert.Equal("admin-user", day.ApprovedByUserId);
        Assert.Equal("Администратор", day.ApprovedByName);
        var notification = Assert.Single(_db.Notifications);
        Assert.Equal("employee-user", notification.UserId);
        Assert.Contains(_db.AuditLogs, log => log.Action == "Approved" && log.EntityId == day.Id.ToString());
    }

    [Fact]
    public async Task SectionMaster_LoadsOwnTimesheet()
    {
        var department = new Department { Name = "Сборка" };
        var master = new Employee
        {
            FullName = "Мастер участка",
            Department = department,
            UserId = "admin-user",
        };
        _db.Employees.Add(master);
        await _db.SaveChangesAsync();
        var model = new Pages.Employee.IndexModel(_db, CurrentUser(AppRoles.SectionMaster));
        SetTempData(model);

        var result = await model.OnGet(null, null);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(result);
        Assert.Equal(master.Id, model.Employee?.Id);
        Assert.Equal(department.Id, model.Employee?.DepartmentId);
    }

    [Fact]
    public async Task AddEmployee_AddsToSelectedDepartment()
    {
        var department = new Department { Name = "Склад" };
        _db.Departments.Add(department);
        await _db.SaveChangesAsync();
        var model = DepartmentsModel();
        model.DepartmentId = department.Id;
        model.EmployeeFullName = "  Иванов Иван  ";
        model.EmployeePosition = "  Кладовщик  ";

        await model.OnPostAddEmployeeAsync();

        var employee = Assert.Single(_db.Employees);
        Assert.Equal("Иванов Иван", employee.FullName);
        Assert.Equal("Кладовщик", employee.Position);
        Assert.Equal(department.Id, employee.DepartmentId);
        Assert.True(employee.IsActive);
    }

    [Fact]
    public async Task DeleteEmployee_WithoutHistoryOrAccount_RemovesEmployee()
    {
        var department = new Department { Name = "Склад" };
        var employee = new Employee { FullName = "Работник", Department = department };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        await DepartmentsModel().OnPostDeleteEmployeeAsync(employee.Id, department.Id);

        Assert.False(await _db.Employees.AnyAsync());
        Assert.Contains(_db.AuditLogs, log => log.Action == "Deleted" && log.EntityName == "Employee");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DeleteEmployee_WithHistoryOrAccount_PreservesEmployee(bool hasWorkDay, bool hasAccount)
    {
        var department = new Department { Name = "Сборка" };
        var employee = new Employee
        {
            FullName = "Работник",
            Department = department,
            UserId = hasAccount ? "employee-user" : null,
        };
        _db.Employees.Add(employee);
        if (hasWorkDay)
            _db.WorkDays.Add(new WorkDay { Employee = employee, Date = new DateOnly(2026, 11, 4) });
        await _db.SaveChangesAsync();

        await DepartmentsModel().OnPostDeleteEmployeeAsync(employee.Id, department.Id);

        Assert.True(await _db.Employees.AnyAsync(e => e.Id == employee.Id));
        Assert.DoesNotContain(_db.AuditLogs, log => log.Action == "Deleted" && log.EntityName == "Employee");
    }

    [Fact]
    public async Task ToggleEmployee_DeactivatesAndReactivates()
    {
        var department = new Department { Name = "Сборка" };
        var employee = new Employee { FullName = "Работник", Department = department };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        var model = DepartmentsModel();

        await model.OnPostToggleEmployeeAsync(employee.Id, department.Id);
        Assert.False(employee.IsActive);
        await model.OnPostToggleEmployeeAsync(employee.Id, department.Id);
        Assert.True(employee.IsActive);
    }

    private CurrentUserService CurrentUser(string role)
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
        return new CurrentUserService(new HttpContextAccessor { HttpContext = httpContext }, _db);
    }

    private DepartmentsModel DepartmentsModel()
    {
        var model = new DepartmentsModel(_db, new AuditService(_db), CurrentUser(AppRoles.Admin));
        SetTempData(model);
        return model;
    }

    private static void SetTempData(PageModel model)
        => model.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());

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
}
