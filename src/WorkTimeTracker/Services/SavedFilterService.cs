using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public class SavedFilterService
{
    public const string MasterPage = "master";
    public const string ProductionPage = "production";
    public const string EngineeringPage = "engineering";
    public const string AuditPage = "audit";
    public const string ReportsPage = "reports";

    private const int MaxJsonBytes = 4096;
    private const string KeyPrefix = "SavedFilters";

    private static readonly IReadOnlyDictionary<string, string[]> AllowedKeys =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [MasterPage] = ["from", "to"],
            [ProductionPage] = ["from", "to"],
            [EngineeringPage] = ["filter", "priority", "search"],
            [AuditPage] = ["user", "entity"],
            [ReportsPage] = ["employeeId", "year", "month", "from", "to", "departmentId"],
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;

    public SavedFilterService(ApplicationDbContext db, CurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string pageKey)
    {
        var allowedKeys = GetAllowedKeys(pageKey);
        var userId = _currentUser.UserId;
        if (userId is null) return new Dictionary<string, string>();

        var setting = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == StorageKey(userId, pageKey));
        return ReadFilters(setting?.Value, allowedKeys);
    }

    public async Task<bool> SaveAsync(string pageKey, IReadOnlyDictionary<string, string?> filters)
    {
        var allowedKeys = GetAllowedKeys(pageKey);
        var userId = _currentUser.UserId;
        if (userId is null) return false;

        var key = StorageKey(userId, pageKey);
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        var current = setting is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ReadFilters(setting.Value, allowedKeys).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        foreach (var (filterKey, value) in filters)
        {
            if (!allowedKeys.Contains(filterKey, StringComparer.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(value))
                current.Remove(filterKey);
            else
                current[filterKey] = value;
        }

        if (current.Count == 0)
        {
            if (setting is not null)
            {
                _db.SystemSettings.Remove(setting);
                await _db.SaveChangesAsync();
            }
            return true;
        }

        var ordered = current.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes) return false;

        if (setting is null)
            _db.SystemSettings.Add(new SystemSetting { Key = key, Value = json });
        else
            setting.Value = json;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ResetAsync(string pageKey)
    {
        GetAllowedKeys(pageKey);
        var userId = _currentUser.UserId;
        if (userId is null) return;

        var setting = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == StorageKey(userId, pageKey));
        if (setting is null) return;
        _db.SystemSettings.Remove(setting);
        await _db.SaveChangesAsync();
    }

    private static string[] GetAllowedKeys(string pageKey)
        => AllowedKeys.TryGetValue(pageKey, out var keys)
            ? keys
            : throw new ArgumentOutOfRangeException(nameof(pageKey), "Неизвестная страница фильтров.");

    private static Dictionary<string, string> ReadFilters(string? json, string[] allowedKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes) return result;

        try
        {
            var filters = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions);
            if (filters is null) return result;
            foreach (var (key, value) in filters)
            {
                if (value is not null && allowedKeys.Contains(key, StringComparer.Ordinal))
                    result[key] = value;
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return result;
    }

    private static string StorageKey(string userId, string pageKey) => $"{KeyPrefix}:{userId}:{pageKey}";
}
