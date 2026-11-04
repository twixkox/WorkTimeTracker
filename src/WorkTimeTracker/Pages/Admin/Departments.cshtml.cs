using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class DepartmentsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public DepartmentsModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public List<Department> Departments { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public bool IsWeeklyView { get; set; }

    [BindProperty]
    public bool IsTimeOnly { get; set; }

    public async Task OnGet()
        => Departments = await _db.Departments.Include(d => d.Employees).OrderBy(d => d.Name).ToListAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            TempData["Error"] = "Укажите название подразделения.";
            return RedirectToPage();
        }

        var weekly = IsWeeklyView && !IsTimeOnly;
        Department? dept;
        if (EditId is int id && (dept = await _db.Departments.FindAsync(id)) is not null)
        {
            dept.Name = Name.Trim();
            dept.IsWeeklyView = weekly;
            dept.IsTimeOnly = IsTimeOnly;
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "Department", id.ToString(), Name);
        }
        else
        {
            _db.Departments.Add(new Department { Name = Name.Trim(), IsWeeklyView = weekly, IsTimeOnly = IsTimeOnly });
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Created", "Department", null, Name);
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Сохранено.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept is not null)
        {
            dept.IsActive = !dept.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, dept.IsActive ? "Activated" : "Deactivated", "Department", id.ToString(), dept.Name);
        }
        return RedirectToPage();
    }
}
