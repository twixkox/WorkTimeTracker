using Microsoft.AspNetCore.Html;

namespace WorkTimeTracker.Pages;

public static class ViewHelpers
{
    /// <summary>Цветной бейдж статуса табеля.</summary>
    public static IHtmlContent StatusBadge(Models.TimeSheetStatus status)
    {
        var (cls, text) = status switch
        {
            Models.TimeSheetStatus.Draft => ("bg-secondary", "Черновик"),
            Models.TimeSheetStatus.Submitted => ("bg-warning text-dark", "На утверждении"),
            Models.TimeSheetStatus.Approved => ("bg-success", "Утверждён"),
            Models.TimeSheetStatus.Rejected => ("bg-danger", "Отклонён"),
            Models.TimeSheetStatus.Recorded => ("bg-success-subtle text-success-emphasis", "Сохранено"),
            _ => ("bg-light text-dark", status.ToString()),
        };
        return new HtmlString($"<span class=\"badge status-badge {cls}\">{text}</span>");
    }
}
