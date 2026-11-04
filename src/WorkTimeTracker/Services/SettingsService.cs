using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public static class ShiftDefaults
{
    public static readonly TimeOnly Start = new(9, 0);
    public static readonly TimeOnly End = new(18, 0);
}

/// <summary>Чтение настроек системы из БД с значениями по умолчанию.</summary>
public class SettingsService
{
    private readonly ApplicationDbContext _db;

    public SettingsService(ApplicationDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key)
        => (await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task SetAsync(string key, string value)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting is null)
            _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        else
            setting.Value = value;
        await _db.SaveChangesAsync();
    }

    public async Task<ValidationSettings> GetValidationSettingsAsync()
    {
        var s = ValidationSettings.Defaults;
        var overlap = await GetAsync(SettingKeys.CheckIntervalOverlap);

        return s with
        {
            MaxHoursPerDay = await GetDecimalAsync(SettingKeys.MaxHoursPerDay, s.MaxHoursPerDay),
            OvertimeThreshold = await GetDecimalAsync(SettingKeys.OvertimeThreshold, s.OvertimeThreshold),
            CheckIntervalOverlap = overlap is null || bool.Parse(overlap),
        };
    }

    public async Task<bool> IsTwoStageApprovalAsync()
    {
        var v = await GetAsync(SettingKeys.TwoStageApproval);
        return v is not null && bool.Parse(v);
    }

    public async Task<(TimeOnly Start, TimeOnly End)?> GetShiftTemplateAsync()
    {
        var start = await GetAsync(SettingKeys.ShiftStart);
        var end = await GetAsync(SettingKeys.ShiftEnd);
        if (TimeOnly.TryParse(start, out var s) && TimeOnly.TryParse(end, out var e))
            return (s, e);
        return null;
    }

    private async Task<decimal> GetDecimalAsync(string key, decimal def)
    {
        var v = await GetAsync(key);
        return decimal.TryParse(v, out var d) ? d : def;
    }
}
