using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class BoatsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public BoatsModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public List<Boat> Boats { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? EditId { get; set; }
    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Customer { get; set; }

    public async Task OnGet()
        => Boats = await _db.Boats.OrderBy(b => b.Number).ToListAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Number))
        {
            TempData["Error"] = "Укажите номер катера.";
            return RedirectToPage();
        }

        Boat? boat;
        if (EditId is int id && (boat = await _db.Boats.FindAsync(id)) is not null)
        {
            boat.Number = Number.Trim();
            boat.Name = Name;
            boat.Customer = Customer;
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "Boat", id.ToString(), Number);
        }
        else
        {
            if (await _db.Boats.AnyAsync(b => b.Number == Number.Trim()))
            {
                TempData["Error"] = $"Катер с номером «{Number.Trim()}» уже существует.";
                return RedirectToPage();
            }
            _db.Boats.Add(new Boat { Number = Number.Trim(), Name = Name, Customer = Customer });
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Created", "Boat", null, Number);
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Сохранено.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var boat = await _db.Boats.FindAsync(id);
        if (boat is not null)
        {
            boat.IsActive = !boat.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, boat.IsActive ? "Activated" : "Deactivated", "Boat", id.ToString(), boat.Number);
        }
        return RedirectToPage();
    }
}
