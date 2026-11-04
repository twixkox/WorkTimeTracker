using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public sealed class DatabaseLoggerProvider : ILoggerProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DatabaseLoggerProvider(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public ILogger CreateLogger(string categoryName) =>
        new DatabaseLogger(categoryName, _scopeFactory, _httpContextAccessor);

    public void Dispose()
    {
    }
}

internal sealed class DatabaseLogger : ILogger
{
    private readonly string _category;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DatabaseLogger(string category, IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _category = category;
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Information && logLevel != LogLevel.None &&
        !_category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var log = new ApplicationLog
            {
                Level = logLevel,
                Category = _category,
                Message = formatter(state, exception),
                Exception = exception?.ToString(),
                UserName = httpContext?.User.Identity?.IsAuthenticated == true
                    ? AuditService.UserName(httpContext.User)
                    : null,
                RequestPath = httpContext is null ? null : $"{httpContext.Request.Method} {httpContext.Request.Path}{httpContext.Request.QueryString}",
            };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ApplicationLogs.Add(log);
            db.SaveChanges();
        }
        catch
        {
            // Сбой журналирования не должен прерывать основной запрос.
        }
    }
}
