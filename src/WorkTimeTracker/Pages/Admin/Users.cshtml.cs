using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class UsersModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public UsersModel(UserManager<ApplicationUser> userManager, ApplicationDbContext db,
        AuditService audit, CurrentUserService currentUser)
    {
        _userManager = userManager;
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public List<(ApplicationUser User, IList<string> Roles, int? EmployeeId, string? EmployeeName)> Users { get; } = new();
    public SelectList Employees { get; private set; } = default!;
    public SelectList RoleList { get; private set; } = default!;

    [BindProperty]
    public string? EditUserId { get; set; }
    [BindProperty]
    public string Role { get; set; } = AppRoles.Employee;
    [BindProperty]
    public int? BindEmployeeId { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == u.Id);
            Users.Add((u, roles, employee?.Id, employee?.FullName));
        }
        var boundIds = Users.Select(t => t.EmployeeId).ToHashSet();
        var freeEmployees = (await _db.Employees
                .Where(e => e.IsActive && e.UserId == null)
                .OrderBy(e => e.FullName)
                .ToListAsync())
            .Concat(await _db.Employees
                .Where(e => e.IsActive && e.UserId != null && boundIds.Contains(e.Id))
                .OrderBy(e => e.FullName)
                .ToListAsync())
            .OrderBy(e => e.FullName)
            .ToList();
        Employees = new SelectList(freeEmployees, "Id", "FullName");
        RoleList = new SelectList(AppRoles.All);
    }

    public async Task<IActionResult> OnPostSetRoleAsync(string userId, string role)
    {
        if (!AppRoles.All.Contains(role))
        {
            TempData["Error"] = "Выберите одну роль из списка.";
            return RedirectToPage();
        }
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        var current = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, current);
        await _userManager.AddToRoleAsync(user, role);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "RoleChanged", "ApplicationUser", userId, $"{user.Email}: {string.Join(",", current)} → {role}");
        TempData["Success"] = "Роль обновлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBindEmployeeAsync(string userId, int employeeId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        var employee = await _db.Employees.FindAsync(employeeId);
        if (user is null || employee is null) return NotFound();

        // Снимаем привязку с прежней карточки.
        var old = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
        if (old is not null) old.UserId = null;

        var occupied = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id && e.Id != employeeId);
        if (occupied is not null) occupied.UserId = null;

        employee.UserId = user.Id;
        user.DisplayName = employee.FullName;
        await _db.SaveChangesAsync();
        await _userManager.UpdateAsync(user);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Bound", "Employee", employeeId.ToString(), $"{employee.FullName} ↔ {user.Email}");
        TempData["Success"] = "Сотрудник привязан к учётной записи.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, user.IsActive ? "Activated" : "Deactivated", "ApplicationUser", userId, user.Email);
        TempData["Success"] = user.IsActive ? "Пользователь активирован." : "Пользователь деактивирован.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var newPassword = $"R{Guid.NewGuid():N}a1!";
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "PasswordReset", "ApplicationUser", userId, user.Email);
            TempData["Success"] = $"Новый пароль (показывается один раз): {newPassword}";
        }
        else
        {
            TempData["Error"] = string.Join("; ", result.Errors.Select(e => e.Description));
        }
        return RedirectToPage();
    }
}
