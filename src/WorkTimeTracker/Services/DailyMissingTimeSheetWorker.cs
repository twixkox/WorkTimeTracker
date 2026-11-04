namespace WorkTimeTracker.Services;

public class DailyMissingTimeSheetWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyMissingTimeSheetWorker> _logger;

    public DailyMissingTimeSheetWorker(IServiceScopeFactory scopeFactory,
        ILogger<DailyMissingTimeSheetWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var shift = await settings.GetShiftTemplateAsync();
                var shiftEnd = shift?.End ?? ShiftDefaults.End;
                var now = DateTime.Now;

                if (now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
                    TimeOnly.FromDateTime(now) >= shiftEnd)
                {
                    var sender = scope.ServiceProvider.GetRequiredService<MissingTimeSheetNotificationService>();
                    var sent = await sender.SendDailySummariesAsync(DateOnly.FromDateTime(now));
                    if (sent > 0)
                        _logger.LogInformation("Отправлено сводных уведомлений о незаполненных табелях: {Count}", sent);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Не удалось отправить сводные уведомления о незаполненных табелях");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
