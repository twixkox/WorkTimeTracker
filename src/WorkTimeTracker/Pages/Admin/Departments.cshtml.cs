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
    public Department? SelectedDepartment { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DepartmentId { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public bool IsWeeklyView { get; set; }

    [BindProperty]
    public bool IsTimeOnly { get; set; }

    [BindProperty]
    public string EmployeeFullName { get; set; } = string.Empty;

    [BindProperty]
    public string? EmployeePosition { get; set; }

    public async Task OnGet()
    {
        Departments = await _db.Departments.Include(d => d.Employees).OrderBy(d => d.Name).ToListAsync();
        SelectedDepartment = Departments.FirstOrDefault(d => d.Id == DepartmentId);
        if (SelectedDepartment is not null)
            SelectedDepartment.Employees = SelectedDepartment.Employees
                .OrderByDescending(e => e.IsActive).ThenBy(e => e.FullName).ToList();
    }

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

    public async Task<IActionResult> OnPostAddEmployeeAsync()
    {
        if (DepartmentId is not int departmentId) return NotFound();
        var department = await _db.Departments.FindAsync(departmentId);
        if (department is null) return NotFound();
        if (string.IsNullOrWhiteSpace(EmployeeFullName))
        {
            TempData["Error"] = "Укажите ФИО работника.";
            return RedirectToEmployees(departmentId);
        }

        var employee = new Models.Employee
        {
            FullName = EmployeeFullName.Trim(),
            Position = string.IsNullOrWhiteSpace(EmployeePosition) ? null : EmployeePosition.Trim(),
            DepartmentId = departmentId,
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Created", "Employee", employee.Id.ToString(),
            $"{employee.FullName}; подразделение: {department.Name}");
        TempData["Success"] = $"Работник «{employee.FullName}» добавлен.";
        return RedirectToEmployees(departmentId);
    }

    public async Task<IActionResult> OnPostDeleteEmployeeAsync(int id, int departmentId)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.DepartmentId == departmentId);
        if (employee is null) return NotFound();
        if (employee.UserId is not null || await _db.WorkDays.AnyAsync(w => w.EmployeeId == id))
        {
            TempData["Error"] = "Нельзя удалить работника с табелями или привязанной учётной записью. Деактивируйте его, чтобы сохранить историю.";
            return RedirectToEmployees(departmentId);
        }

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Deleted", "Employee", id.ToString(),
            $"{employee.FullName}; подразделение: {departmentId}");
        TempData["Success"] = $"Работник «{employee.FullName}» удалён.";
        return RedirectToEmployees(departmentId);
    }

    public async Task<IActionResult> OnPostToggleEmployeeAsync(int id, int departmentId)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.DepartmentId == departmentId);
        if (employee is null) return NotFound();
        employee.IsActive = !employee.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName,
            employee.IsActive ? "Activated" : "Deactivated", "Employee", id.ToString(), employee.FullName);
        TempData["Success"] = employee.IsActive ? "Работник активирован." : "Работник деактивирован.";
        return RedirectToEmployees(departmentId);
    }

    private IActionResult RedirectToEmployees(int departmentId)
        => RedirectToPage("/Admin/Departments", null, new { DepartmentId = departmentId }, "employees");
}
