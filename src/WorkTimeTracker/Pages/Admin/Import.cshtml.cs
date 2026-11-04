using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class ImportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ExcelService _excel;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public ImportModel(ApplicationDbContext db, ExcelService excel, AuditService audit, CurrentUserService currentUser)
    {
        _db = db;
        _excel = excel;
        _audit = audit;
        _currentUser = currentUser;
    }

    public string? ResultMessage { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostBoatsAsync(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Выберите файл Excel.";
            return RedirectToPage();
        }

        int added = 0, skipped = 0;
        using var stream = file.OpenReadStream();
        foreach (var (number, name, customer) in _excel.ImportBoats(stream))
        {
            if (await _db.Boats.AnyAsync(b => b.Number == number))
            {
                skipped++;
                continue;
            }
            _db.Boats.Add(new Boat { Number = number, Name = name, Customer = customer });
            added++;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Imported", "Boat", null, $"файл {file.FileName}: добавлено {added}, пропущено {skipped}");
        TempData["Success"] = $"Импорт катеров завершён: добавлено {added}, пропущено {skipped} (дубликаты).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEmployeesAsync(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Выберите файл Excel.";
            return RedirectToPage();
        }

        int added = 0, skipped = 0, newDepts = 0;
        using var stream = file.OpenReadStream();
        foreach (var (fullName, deptName, position) in _excel.ImportEmployees(stream))
        {
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Name == deptName);
            if (dept is null)
            {
                dept = new Department { Name = deptName };
                _db.Departments.Add(dept);
                await _db.SaveChangesAsync();
                newDepts++;
            }
            if (await _db.Employees.AnyAsync(e => e.FullName == fullName && e.DepartmentId == dept.Id))
            {
                skipped++;
                continue;
            }
            _db.Employees.Add(new Models.Employee { FullName = fullName, DepartmentId = dept.Id, Position = position });
            added++;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Imported", "Employee", null, $"файл {file.FileName}: добавлено {added}, пропущено {skipped}, новых подразделений {newDepts}");
        TempData["Success"] = $"Импорт сотрудников завершён: добавлено {added}, пропущено {skipped}, создано подразделений: {newDepts}.";
        return RedirectToPage();
    }
}
