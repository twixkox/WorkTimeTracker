namespace WorkTimeTracker.Services;

public class EngineeringTaskReminderWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EngineeringTaskReminderWorker> _logger;

    public EngineeringTaskReminderWorker(IServiceScopeFactory scopeFactory,
        ILogger<EngineeringTaskReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<EngineeringTaskReminderService>();
                var sent = await reminders.SendRemindersAsync(DateOnly.FromDateTime(DateTime.Now));
                if (sent > 0)
                    _logger.LogInformation("Отправлено напоминаний о дедлайнах задач КБ: {Count}", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Не удалось отправить напоминания о дедлайнах задач КБ");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
