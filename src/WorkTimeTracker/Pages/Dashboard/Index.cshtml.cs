using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Dashboard;

[Authorize]
public class IndexModel : PageModel
{
    private readonly PersonalDashboardService _dashboard;

    public IndexModel(PersonalDashboardService dashboard) => _dashboard = dashboard;

    public PersonalDashboardViewModel Dashboard { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => Dashboard = await _dashboard.GetDashboardAsync(cancellationToken: cancellationToken);
}
