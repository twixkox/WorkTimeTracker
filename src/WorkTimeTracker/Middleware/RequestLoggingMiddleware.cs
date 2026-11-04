using System.Diagnostics;

namespace WorkTimeTracker.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Необработанная ошибка запроса {Method} {Path}",
                context.Request.Method, context.Request.Path);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var status = context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            _logger.Log(level, "HTTP {Method} {Path}: {StatusCode}, {Elapsed} мс",
                context.Request.Method, context.Request.Path, status, stopwatch.ElapsedMilliseconds);
        }
    }
}
