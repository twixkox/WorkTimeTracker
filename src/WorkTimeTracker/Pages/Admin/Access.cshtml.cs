using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Security;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class AccessModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IAccessService _access;
    private readonly CurrentUserService _currentUser;
    private readonly AuditService _audit;

    public AccessModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager, IAccessService access, CurrentUserService currentUser,
        AuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _access = access;
        _currentUser = currentUser;
        _audit = audit;
    }

    public List<UserAccessRow> Users { get; } = new();
    public List<Department> Departments { get; private set; } = new();

    [BindProperty]
    public string ScopeUserId { get; set; } = string.Empty;
    [BindProperty]
    public string ScopeRole { get; set; } = string.Empty;
    [BindProperty]
    public AccessScope Scope { get; set; }
    [BindProperty]
    public int? DepartmentId { get; set; }
    [BindProperty]
    public DateTime? ValidFrom { get; set; }
    [BindProperty]
    public DateTime? ValidTo { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveRolesAsync(string userId, string[] roles)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        var selected = roles.Where(AppRoles.All.Contains).Distinct().ToArray();
        var current = await _userManager.GetRolesAsync(user);
        if (current.Contains(AppRoles.Admin) && !selected.Contains(AppRoles.Admin) && await IsLastActiveAdminAsync(user.Id))
        {
            TempData["Error"] = "Нельзя удалить роль последнего активного администратора.";
            return RedirectToPage();
        }

        var toRemove = current.Except(selected).ToArray();
        var toAdd = selected.Except(current).ToArray();
        if (toRemove.Length > 0)
        {
            var result = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!result.Succeeded) return RoleError(result);
        }
        if (toAdd.Length > 0)
        {
            var result = await _userManager.AddToRolesAsync(user, toAdd);
            if (!result.Succeeded) return RoleError(result);
        }

        if (toRemove.Length > 0)
        {
            var roleIds = await _roleManager.Roles.Where(r => toRemove.Contains(r.Name!)).Select(r => r.Id).ToListAsync();
            var obsolete = await _db.UserRoleScopes.Where(s => s.UserId == userId && roleIds.Contains(s.RoleId)).ToListAsync();
            _db.UserRoleScopes.RemoveRange(obsolete);
            await _db.SaveChangesAsync();
        }
        await _userManager.UpdateSecurityStampAsync(user);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "RolesChanged", "ApplicationUser",
            userId, $"{user.Email}: {string.Join(",", current)} → {string.Join(",", selected)}");
        TempData["Success"] = "Роли и область действия обновлены.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddScopeAsync()
    {
        var user = await _userManager.FindByIdAsync(ScopeUserId);
        var role = await _roleManager.FindByNameAsync(ScopeRole);
        if (user is null || role is null) return NotFound();
        if (!await _userManager.IsInRoleAsync(user, ScopeRole) || !Enum.IsDefined(Scope))
        {
            TempData["Error"] = "Выберите назначенную пользователю роль и корректную область.";
            return RedirectToPage();
        }
        if (ValidFrom.HasValue && ValidTo.HasValue && ValidTo < ValidFrom)
        {
            TempData["Error"] = "Окончание срока не может быть раньше начала.";
            return RedirectToPage();
        }
        if (Scope == AccessScope.Department)
        {
            if (DepartmentId is null || !await _db.Departments.AnyAsync(d => d.Id == DepartmentId))
            {
                TempData["Error"] = "Для участковой области выберите подразделение.";
                return RedirectToPage();
            }
        }
        else DepartmentId = null;

        var duplicate = await _db.UserRoleScopes.AnyAsync(s => s.UserId == ScopeUserId && s.RoleId == role.Id
            && s.Scope == Scope && s.DepartmentId == DepartmentId);
        if (!duplicate)
        {
            _db.UserRoleScopes.Add(new UserRoleScope
            {
                UserId = ScopeUserId,
                RoleId = role.Id,
                Scope = Scope,
                DepartmentId = DepartmentId,
                ValidFrom = ValidFrom,
                ValidTo = ValidTo,
            });
            await _db.SaveChangesAsync();
            await _userManager.UpdateSecurityStampAsync(user);
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "ScopeAdded", "UserRoleScope",
                user.Id, $"{user.Email}; {ScopeRole}; {Scope}; department={DepartmentId}; {ValidFrom:o}—{ValidTo:o}");
        }
        TempData["Success"] = duplicate ? "Такая область уже существует." : "Область доступа добавлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteScopeAsync(int id)
    {
        var scope = await _db.UserRoleScopes.Include(s => s.User).Include(s => s.Role)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (scope is null) return NotFound();
        var details = $"{scope.User?.Email}; {scope.Role?.Name}; {scope.Scope}; department={scope.DepartmentId}";
        _db.UserRoleScopes.Remove(scope);
        await _db.SaveChangesAsync();
        if (scope.User is not null) await _userManager.UpdateSecurityStampAsync(scope.User);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "ScopeDeleted", "UserRoleScope",
            id.ToString(), details);
        TempData["Success"] = "Область удалена. Если других областей роли нет, действует стандартная область роли.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();
        var scopes = await _db.UserRoleScopes.Include(s => s.Role).Include(s => s.Department).AsNoTracking().ToListAsync();
        foreach (var user in await _userManager.Users.OrderBy(u => u.Email).ToListAsync())
        {
            var roles = await _userManager.GetRolesAsync(user);
            var permissions = await _access.GetPermissionsAsync(user.Id);
            Users.Add(new UserAccessRow(user, roles, permissions,
                scopes.Where(s => s.UserId == user.Id).OrderBy(s => s.Role?.Name).ThenBy(s => s.Id).ToList()));
        }
    }

    private async Task<bool> IsLastActiveAdminAsync(string userId)
    {
        var admins = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);
        return !admins.Any(u => u.IsActive && u.Id != userId);
    }

    private IActionResult RoleError(IdentityResult result)
    {
        TempData["Error"] = string.Join("; ", result.Errors.Select(e => e.Description));
        return RedirectToPage();
    }

    public sealed record UserAccessRow(ApplicationUser User, IList<string> Roles,
        IReadOnlyCollection<string> Permissions, List<UserRoleScope> Scopes);
}
