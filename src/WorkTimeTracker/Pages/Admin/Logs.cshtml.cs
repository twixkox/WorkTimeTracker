using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Pages.Admin;

public class LogsModel : PageModel
{
    private const int PageSize = 50;
    private readonly ApplicationDbContext _db;

    public LogsModel(ApplicationDbContext db) => _db = db;

    public List<ApplicationLog> Logs { get; private set; } = new();
    public new int Page { get; private set; }
    public int TotalPages { get; private set; }
    public int ErrorCount { get; private set; }
    public int WarningCount { get; private set; }
    public int InformationCount { get; private set; }
    public LogLevel? Level { get; private set; }
    public string? Search { get; private set; }

    public async Task OnGet(int p = 1, LogLevel? level = null, string? search = null)
    {
        Page = Math.Max(1, p);
        Level = level;
        Search = search;

        var query = _db.ApplicationLogs.AsQueryable();
        ErrorCount = await query.CountAsync(l => l.Level >= LogLevel.Error);
        WarningCount = await query.CountAsync(l => l.Level == LogLevel.Warning);
        InformationCount = await query.CountAsync(l => l.Level == LogLevel.Information);

        if (level == LogLevel.Error)
            query = query.Where(l => l.Level >= LogLevel.Error);
        else if (level is not null)
            query = query.Where(l => l.Level == level);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(l => l.Message.Contains(search) ||
                                     (l.UserName != null && l.UserName.Contains(search)) ||
                                     (l.RequestPath != null && l.RequestPath.Contains(search)));

        var total = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Page = Math.Min(Page, TotalPages);
        Logs = await query.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id)
            .Skip((Page - 1) * PageSize).Take(PageSize).ToListAsync();
    }
}
