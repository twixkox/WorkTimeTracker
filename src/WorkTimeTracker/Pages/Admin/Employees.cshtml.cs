using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class EmployeesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public EmployeesModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public List<Models.Employee> Employees { get; private set; } = new();
    public SelectList Departments { get; private set; } = default!;

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }
    [BindProperty]
    public string FullName { get; set; } = string.Empty;
    [BindProperty]
    public int DepartmentId { get; set; }
    [BindProperty]
    public string? Position { get; set; }

    public async Task OnGet()
    {
        Employees = await _db.Employees.Include(e => e.Department).OrderBy(e => e.Department!.Name).ThenBy(e => e.FullName).ToListAsync();
        var selectedDepartment = Employees.FirstOrDefault(e => e.Id == EditId)?.DepartmentId;
        Departments = new SelectList(await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(), "Id", "Name", selectedDepartment);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            TempData["Error"] = "Укажите ФИО.";
            return RedirectToPage();
        }

        Models.Employee? emp;
        if (EditId is int id && (emp = await _db.Employees.FindAsync(id)) is not null)
        {
            emp.FullName = FullName.Trim();
            emp.DepartmentId = DepartmentId;
            emp.Position = Position;
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "Employee", id.ToString(), FullName);
        }
        else
        {
            _db.Employees.Add(new Models.Employee { FullName = FullName.Trim(), DepartmentId = DepartmentId, Position = Position });
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Created", "Employee", null, FullName);
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Сохранено.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var employee = await _db.Employees.FindAsync(id);
        if (employee is null) return NotFound();
        if (employee.UserId is not null || await _db.WorkDays.AnyAsync(w => w.EmployeeId == id))
        {
            TempData["Error"] = "Нельзя удалить работника с табелями или привязанной учётной записью. Деактивируйте его, чтобы сохранить историю.";
            return RedirectToPage();
        }

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Deleted", "Employee", id.ToString(), employee.FullName);
        TempData["Success"] = $"Работник «{employee.FullName}» удалён.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp is not null)
        {
            emp.IsActive = !emp.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, emp.IsActive ? "Activated" : "Deactivated", "Employee", id.ToString(), emp.FullName);
        }
        return RedirectToPage();
    }
}
