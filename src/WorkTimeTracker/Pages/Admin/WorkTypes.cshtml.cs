using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class WorkTypesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public WorkTypesModel(ApplicationDbContext db, AuditService audit, CurrentUserService currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public List<WorkType> WorkTypes { get; private set; } = new();

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    public async Task OnGet()
        => WorkTypes = await _db.WorkTypes.OrderBy(w => w.Name).ToListAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (!string.IsNullOrWhiteSpace(Name) && !await _db.WorkTypes.AnyAsync(w => w.Name == Name.Trim()))
        {
            _db.WorkTypes.Add(new WorkType { Name = Name.Trim() });
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Created", "WorkType", null, Name);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var wt = await _db.WorkTypes.FindAsync(id);
        if (wt is not null)
        {
            _db.WorkTypes.Remove(wt);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Deleted", "WorkType", id.ToString(), wt.Name);
        }
        return RedirectToPage();
    }
}
