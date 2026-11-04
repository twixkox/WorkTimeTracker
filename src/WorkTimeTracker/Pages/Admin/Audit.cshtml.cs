using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.Pages.Admin;

public class AuditModel : PageModel
{
    private const int PageSize = 50;

    private readonly ApplicationDbContext _db;

    public AuditModel(ApplicationDbContext db) => _db = db;

    public List<Models.AuditLog> Logs { get; private set; } = new();
    public new int Page { get; private set; }
    public int TotalPages { get; private set; }
    public string? FilterUser { get; private set; }
    public string? FilterEntity { get; private set; }

    public async Task OnGet(int p = 1, string? user = null, string? entity = null)
    {
        Page = Math.Max(1, p);
        FilterUser = user;
        FilterEntity = entity;

        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(user))
            q = q.Where(l => l.UserName.Contains(user));
        if (!string.IsNullOrWhiteSpace(entity))
            q = q.Where(l => l.EntityName.Contains(entity));

        var total = await q.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Min(Page, TotalPages);

        Logs = await q.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .ToListAsync();
    }
}
