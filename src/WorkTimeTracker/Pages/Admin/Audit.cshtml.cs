using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class AuditModel : PageModel
{
    private const int PageSize = 50;

    private readonly ApplicationDbContext _db;
    private readonly SavedFilterService _savedFilters;

    public AuditModel(ApplicationDbContext db, SavedFilterService savedFilters)
    {
        _db = db;
        _savedFilters = savedFilters;
    }

    public List<Models.AuditLog> Logs { get; private set; } = new();
    public new int Page { get; private set; }
    public int TotalPages { get; private set; }
    public string? FilterUser { get; private set; }
    public string? FilterEntity { get; private set; }
    public bool FiltersRestored { get; private set; }

    public async Task OnGet(int p = 1, string? user = null, string? entity = null)
    {
        var hasQuery = Request.Query.Count > 0;
        var reset = Request.Query.ContainsKey("reset");
        if (!hasQuery)
        {
            var saved = await _savedFilters.GetAsync(SavedFilterService.AuditPage);
            FiltersRestored = saved.Count > 0;
            user = saved.GetValueOrDefault("user");
            entity = saved.GetValueOrDefault("entity");
        }

        Page = Math.Max(1, p);
        FilterUser = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        FilterEntity = string.IsNullOrWhiteSpace(entity) ? null : entity.Trim();

        if (reset)
            await _savedFilters.ResetAsync(SavedFilterService.AuditPage);
        else if (hasQuery)
            await _savedFilters.SaveAsync(SavedFilterService.AuditPage, new Dictionary<string, string?>
            {
                ["user"] = FilterUser,
                ["entity"] = FilterEntity,
            });

        var q = _db.AuditLogs.AsQueryable();
        if (FilterUser is not null)
            q = q.Where(l => l.UserName.Contains(FilterUser));
        if (FilterEntity is not null)
            q = q.Where(l => l.EntityName.Contains(FilterEntity));

        var total = await q.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Min(Page, TotalPages);

        Logs = await q.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id)
            .Skip((Page - 1) * PageSize).Take(PageSize)
            .ToListAsync();
    }
}
