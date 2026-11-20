using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class NotificationPreferenceServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly NotificationPreferenceService _service;

    public NotificationPreferenceServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _service = new NotificationPreferenceService(_db);
    }

    [Fact]
    public async Task GetAsync_WithoutSettingReturnsDefaultsThatPreserveDelivery()
    {
        var preferences = await _service.GetAsync("user");

        Assert.True(preferences.InAppTimesheet);
        Assert.True(preferences.InAppEngineering);
        Assert.True(preferences.InAppSystem);
        Assert.True(preferences.TelegramEnabled);
        Assert.True(preferences.TelegramTimesheet);
        Assert.True(preferences.TelegramEngineering);
        Assert.True(preferences.TelegramSystem);
        Assert.False(preferences.QuietHoursEnabled);
    }

    [Fact]
    public async Task GetAsync_WithMalformedJsonReturnsDefaults()
    {
        _db.SystemSettings.Add(new SystemSetting
        {
            Key = "Notifications:Preferences:user",
            Value = "{not-json",
        });
        await _db.SaveChangesAsync();

        var preferences = await _service.GetAsync("user");

        Assert.True(preferences.InAppSystem);
        Assert.True(preferences.TelegramEnabled);
        Assert.False(preferences.QuietHoursEnabled);
    }

    [Fact]
    public async Task SaveAsync_StoresIndependentJsonSettingsPerUser()
    {
        var expected = new NotificationPreferences
        {
            InAppTimesheet = false,
            TelegramEngineering = false,
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(23, 30),
            QuietHoursEnd = new TimeOnly(6, 45),
        };

        await _service.SaveAsync("user", expected);
        await _service.SaveAsync("user", expected);

        var actual = await _service.GetAsync("user");
        var otherUser = await _service.GetAsync("other");
        Assert.False(actual.InAppTimesheet);
        Assert.False(actual.TelegramEngineering);
        Assert.True(actual.QuietHoursEnabled);
        Assert.Equal(expected.QuietHoursStart, actual.QuietHoursStart);
        Assert.Equal(expected.QuietHoursEnd, actual.QuietHoursEnd);
        Assert.True(otherUser.InAppTimesheet);
        Assert.True(otherUser.TelegramEngineering);
        Assert.Equal(1, await _db.SystemSettings.CountAsync());
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(7, 59, true)]
    [InlineData(8, 0, false)]
    [InlineData(12, 0, false)]
    public void IsQuietHours_SupportsPeriodCrossingMidnight(int hour, int minute, bool expected)
    {
        var preferences = new NotificationPreferences
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(8, 0),
        };

        Assert.Equal(expected, preferences.IsQuietHours(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void IsQuietHours_EqualBoundariesDoNotSuppressDelivery()
    {
        var preferences = new NotificationPreferences
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(22, 0),
        };

        Assert.False(preferences.IsQuietHours(new TimeOnly(22, 0)));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
