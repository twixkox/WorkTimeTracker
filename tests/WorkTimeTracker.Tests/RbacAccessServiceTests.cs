using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Security;
using Xunit;

namespace WorkTimeTracker.Tests;

public class RbacAccessServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AccessService _service;

    public RbacAccessServiceTests()
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
        _service = new AccessService(_db, _userManager, _roleManager);
    }

    [Fact]
    public async Task MultiRole_DoesNotCombinePermissionWithScopeFromAnotherRole()
    {
        var user = await CreateUserAsync(AppRoles.Employee, AppRoles.SectionMaster);
        await GrantAsync(AppRoles.Employee, AppPermissions.TimeSheetsView);
        await GrantAsync(AppRoles.SectionMaster, AppPermissions.TimeSheetsApprove);
        var department = new Department { Name = "Сборка" };
        _db.Departments.Add(department);
        await _db.SaveChangesAsync();
        await AddScopeAsync(user, AppRoles.Employee, AccessScope.Own);
        await AddScopeAsync(user, AppRoles.SectionMaster, AccessScope.Department, department.Id);

        Assert.False(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsView,
            ownerUserId: "another-user", departmentId: department.Id));
        Assert.True(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove,
            ownerUserId: "another-user", departmentId: department.Id));
        Assert.True(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsView,
            ownerUserId: user.Id));
        Assert.False(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove,
            ownerUserId: user.Id));

        var permissions = await _service.GetPermissionsAsync(user.Id);
        Assert.Contains(AppPermissions.TimeSheetsView, permissions);
        Assert.Contains(AppPermissions.TimeSheetsApprove, permissions);
    }

    [Fact]
    public async Task ExpiredScope_DeniesAccessWithoutLegacyFallback()
    {
        var user = await CreateUserAsync(AppRoles.SectionMaster);
        await GrantAsync(AppRoles.SectionMaster, AppPermissions.TimeSheetsApprove);
        var employee = new Employee
        {
            FullName = "Мастер",
            UserId = user.Id,
            Department = new Department { Name = "Сборка" },
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        await AddScopeAsync(user, AppRoles.SectionMaster, AccessScope.Department,
            employee.DepartmentId, validTo: DateTime.Now.AddMinutes(-1));

        Assert.False(await _service.HasPermissionAsync(user.Id, AppPermissions.TimeSheetsApprove));
        Assert.False(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove,
            departmentId: employee.DepartmentId));
    }

    [Fact]
    public async Task DepartmentScope_DeniesAnotherDepartment()
    {
        var user = await CreateUserAsync(AppRoles.SectionMaster);
        await GrantAsync(AppRoles.SectionMaster, AppPermissions.TimeSheetsApprove);
        var ownDepartment = new Department { Name = "Сборка" };
        var otherDepartment = new Department { Name = "Столярка" };
        _db.Departments.AddRange(ownDepartment, otherDepartment);
        await _db.SaveChangesAsync();
        await AddScopeAsync(user, AppRoles.SectionMaster, AccessScope.Department, ownDepartment.Id);

        Assert.True(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove,
            ownerUserId: "another-user", departmentId: ownDepartment.Id));
        Assert.False(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove,
            ownerUserId: "another-user", departmentId: otherDepartment.Id));
    }

    [Fact]
    public async Task OwnScope_AllowsOnlyResourcesOwnedByUser()
    {
        var user = await CreateUserAsync(AppRoles.Employee);
        await GrantAsync(AppRoles.Employee, AppPermissions.TimeSheetsEdit);
        await AddScopeAsync(user, AppRoles.Employee, AccessScope.Own);

        Assert.True(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsEdit,
            ownerUserId: user.Id, departmentId: 10));
        Assert.False(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsEdit,
            ownerUserId: "another-user", departmentId: 10));
        Assert.False(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsEdit,
            departmentId: 10));
    }

    [Fact]
    public async Task GlobalScope_AllowsAnyOwnerAndDepartment()
    {
        var user = await CreateUserAsync(AppRoles.ProductionHead);
        await GrantAsync(AppRoles.ProductionHead, AppPermissions.TimeSheetsApprove);
        await AddScopeAsync(user, AppRoles.ProductionHead, AccessScope.Global);

        Assert.True(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove,
            ownerUserId: "another-user", departmentId: 20));
        Assert.True(await _service.HasAccessAsync(user.Id, AppPermissions.TimeSheetsApprove));
    }

    [Fact]
    public async Task MissingScopes_AreDerivedFromRolesAndEmployeeDepartment()
    {
        var employeeUser = await CreateUserAsync(AppRoles.Employee);
        await GrantAsync(AppRoles.Employee, AppPermissions.TimeSheetsView);
        Assert.True(await _service.HasAccessAsync(employeeUser.Id, AppPermissions.TimeSheetsView,
            ownerUserId: employeeUser.Id));
        Assert.False(await _service.HasAccessAsync(employeeUser.Id, AppPermissions.TimeSheetsView,
            ownerUserId: "another-user"));

        var master = await CreateUserAsync(AppRoles.SectionMaster);
        await GrantAsync(AppRoles.SectionMaster, AppPermissions.TimeSheetsApprove);
        var department = new Department { Name = "Сборка" };
        _db.Employees.Add(new Employee
        {
            FullName = "Мастер",
            UserId = master.Id,
            Department = department,
        });
        await _db.SaveChangesAsync();

        Assert.True(await _service.HasAccessAsync(master.Id, AppPermissions.TimeSheetsApprove,
            departmentId: department.Id));
        Assert.False(await _service.HasAccessAsync(master.Id, AppPermissions.TimeSheetsApprove,
            departmentId: department.Id + 1));
    }

    [Fact]
    public void AdminPermissions_AreExplicitUnionWithoutEmployeeAndEngineerOwnWork()
    {
        var admin = AppPermissions.ForRole(AppRoles.Admin);

        Assert.Equal(AppPermissions.All
            .Except(new[] { AppPermissions.TimeSheetsSubmit, AppPermissions.EngineeringExecute })
            .OrderBy(p => p), admin.OrderBy(p => p));
        Assert.DoesNotContain(AppPermissions.TimeSheetsSubmit, admin);
        Assert.DoesNotContain(AppPermissions.EngineeringExecute, admin);
    }

    private async Task<ApplicationUser> CreateUserAsync(params string[] roles)
    {
        var user = new ApplicationUser
        {
            UserName = $"{Guid.NewGuid():N}@worktime.local",
            DisplayName = "Тестовый пользователь",
            IsActive = true,
        };
        var result = await _userManager.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        foreach (var roleName in roles)
        {
            if (await _roleManager.FindByNameAsync(roleName) is null)
            {
                result = await _roleManager.CreateAsync(new IdentityRole(roleName));
                Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
            result = await _userManager.AddToRoleAsync(user, roleName);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        return user;
    }

    private async Task GrantAsync(string roleName, params string[] permissions)
    {
        var role = await _roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);
        foreach (var permission in permissions)
        {
            var result = await _roleManager.AddClaimAsync(role!, new Claim(AppPermissions.ClaimType, permission));
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task AddScopeAsync(ApplicationUser user, string roleName, AccessScope scope,
        int? departmentId = null, DateTime? validTo = null)
    {
        var role = await _roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);
        _db.UserRoleScopes.Add(new UserRoleScope
        {
            UserId = user.Id,
            RoleId = role!.Id,
            Scope = scope,
            DepartmentId = departmentId,
            ValidTo = validTo,
        });
        await _db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
