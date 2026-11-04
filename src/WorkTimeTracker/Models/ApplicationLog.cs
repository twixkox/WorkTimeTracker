using Microsoft.Extensions.Logging;

namespace WorkTimeTracker.Models;

public class ApplicationLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? UserName { get; set; }
    public string? RequestPath { get; set; }
}
