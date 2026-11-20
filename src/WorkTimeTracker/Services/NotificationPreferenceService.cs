using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public sealed class NotificationPreferences
{
    public bool InAppTimesheet { get; set; } = true;
    public bool InAppEngineering { get; set; } = true;
    public bool InAppSystem { get; set; } = true;
    public bool TelegramEnabled { get; set; } = true;
    public bool TelegramTimesheet { get; set; } = true;
    public bool TelegramEngineering { get; set; } = true;
    public bool TelegramSystem { get; set; } = true;
    public bool QuietHoursEnabled { get; set; }
    public TimeOnly QuietHoursStart { get; set; } = new(22, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(8, 0);

    public bool IsInAppEnabled(NotificationCategory category) => category switch
    {
        NotificationCategory.Timesheet => InAppTimesheet,
        NotificationCategory.Engineering => InAppEngineering,
        _ => InAppSystem,
    };

    public bool AllowsTelegram(NotificationCategory category, TimeOnly localTime)
        => TelegramEnabled && IsTelegramCategoryEnabled(category) && !IsQuietHours(localTime);

    public bool IsQuietHours(TimeOnly localTime)
    {
        if (!QuietHoursEnabled || QuietHoursStart == QuietHoursEnd)
            return false;

        return QuietHoursStart < QuietHoursEnd
            ? localTime >= QuietHoursStart && localTime < QuietHoursEnd
            : localTime >= QuietHoursStart || localTime < QuietHoursEnd;
    }

    private bool IsTelegramCategoryEnabled(NotificationCategory category) => category switch
    {
        NotificationCategory.Timesheet => TelegramTimesheet,
        NotificationCategory.Engineering => TelegramEngineering,
        _ => TelegramSystem,
    };
}

public class NotificationPreferenceService
{
    private const string KeyPrefix = "Notifications:Preferences:";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ApplicationDbContext _db;

    public NotificationPreferenceService(ApplicationDbContext db) => _db = db;

    public async Task<NotificationPreferences> GetAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var value = (await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KeyPrefix + userId,
            cancellationToken))?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return new NotificationPreferences();

        try
        {
            return JsonSerializer.Deserialize<NotificationPreferences>(value, JsonOptions)
                   ?? new NotificationPreferences();
        }
        catch (JsonException)
        {
            return new NotificationPreferences();
        }
    }

    public async Task SaveAsync(string userId, NotificationPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        var key = KeyPrefix + userId;
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        var value = JsonSerializer.Serialize(preferences, JsonOptions);
        if (setting is null)
            _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        else
            setting.Value = value;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
