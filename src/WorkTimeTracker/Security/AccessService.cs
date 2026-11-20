using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Security;

public class AccessService : IAccessService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AccessService(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return Array.Empty<string>();

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in await GetRolesAsync(user))
        {
            var claims = await _roleManager.GetClaimsAsync(role);
            foreach (var claim in claims.Where(c => c.Type == AppPermissions.ClaimType && !string.IsNullOrWhiteSpace(c.Value)))
                permissions.Add(claim.Value);
        }
        return permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    public async Task<bool> HasPermissionAsync(string userId, string permission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permission)) return false;
        var now = DateTime.Now;
        var roles = await GetRoleAccessAsync(userId, cancellationToken);
        return roles.Any(r => r.Permissions.Contains(permission, StringComparer.Ordinal)
            && r.Scopes.Any(s => IsActive(s, now)));
    }

    public async Task<bool> HasAccessAsync(string userId, string permission, string? ownerUserId = null,
        int? departmentId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permission)) return false;
        var now = DateTime.Now;
        var roles = await GetRoleAccessAsync(userId, cancellationToken);
        return roles.Any(r => r.Permissions.Contains(permission, StringComparer.Ordinal)
            && r.Scopes.Any(s => IsActive(s, now) && Allows(s, userId, ownerUserId, departmentId)));
    }

    private async Task<List<IdentityRole>> GetRolesAsync(ApplicationUser user)
    {
        var roleNames = await _userManager.GetRolesAsync(user);
        var roles = new List<IdentityRole>(roleNames.Count);
        foreach (var roleName in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is not null) roles.Add(role);
        }
        return roles;
    }

    private async Task<List<RoleAccess>> GetRoleAccessAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return new List<RoleAccess>();

        var roles = await GetRolesAsync(user);
        if (roles.Count == 0) return new List<RoleAccess>();

        var roleIds = roles.Select(r => r.Id).ToArray();
        var storedScopes = await _db.UserRoleScopes.AsNoTracking()
            .Where(s => s.UserId == userId && roleIds.Contains(s.RoleId))
            .ToListAsync(cancellationToken);
        var employeeDepartmentId = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Id)
            .Select(e => (int?)e.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        var result = new List<RoleAccess>(roles.Count);
        foreach (var role in roles)
        {
            var roleScopes = storedScopes.Where(s => s.RoleId == role.Id).ToList();
            if (roleScopes.Count == 0)
            {
                var legacyScope = CreateLegacyScope(userId, role, employeeDepartmentId);
                if (legacyScope is not null) roleScopes.Add(legacyScope);
            }

            var claims = await _roleManager.GetClaimsAsync(role);
            var permissions = claims
                .Where(c => c.Type == AppPermissions.ClaimType && !string.IsNullOrWhiteSpace(c.Value))
                .Select(c => c.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            result.Add(new RoleAccess(permissions, roleScopes));
        }
        return result;
    }

    private static UserRoleScope? CreateLegacyScope(string userId, IdentityRole role, int? employeeDepartmentId)
    {
        var scope = role.Name switch
        {
            AppRoles.Employee => AccessScope.Own,
            AppRoles.SectionMaster => AccessScope.Department,
            AppRoles.ProductionHead or AppRoles.ChiefEngineer or AppRoles.Engineer or AppRoles.Admin => AccessScope.Global,
            _ => (AccessScope?)null,
        };
        if (scope is null || (scope == AccessScope.Department && employeeDepartmentId is null)) return null;

        return new UserRoleScope
        {
            UserId = userId,
            RoleId = role.Id,
            Scope = scope.Value,
            DepartmentId = scope == AccessScope.Department ? employeeDepartmentId : null,
        };
    }

    private static bool IsActive(UserRoleScope scope, DateTime now)
        => (scope.ValidFrom is null || scope.ValidFrom <= now)
            && (scope.ValidTo is null || scope.ValidTo >= now);

    private static bool Allows(UserRoleScope scope, string userId, string? ownerUserId, int? departmentId)
        => scope.Scope switch
        {
            AccessScope.Own => ownerUserId is not null && string.Equals(ownerUserId, userId, StringComparison.Ordinal),
            AccessScope.Department => scope.DepartmentId is not null && scope.DepartmentId == departmentId,
            AccessScope.Global => true,
            _ => false,
        };

    private sealed record RoleAccess(string[] Permissions, List<UserRoleScope> Scopes);
}
