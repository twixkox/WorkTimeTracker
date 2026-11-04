using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TimeCalculationServiceTests
{
    private static TimeEntry Entry(decimal hours, int? boatId = 1, bool inhouse = false, string work = "Работа")
        => new() { Hours = hours, BoatId = inhouse ? null : boatId, IsInhouse = inhouse, WorkDescription = work };

    [Fact]
    public void WorkedHours_SubtractsLunchHour()
    {
        Assert.Equal(7.5m, TimeCalculationService.WorkedHours(new TimeOnly(8, 0), new TimeOnly(16, 30)));
        Assert.Equal(0m, TimeCalculationService.WorkedHours(new TimeOnly(8, 0), new TimeOnly(8, 30)));
        Assert.Equal(0m, TimeCalculationService.WorkedHours(new TimeOnly(16, 30), new TimeOnly(8, 0)));
    }

    [Fact]
    public void EntriesHours_SumsAllEntries()
    {
        var entries = new List<TimeEntry> { Entry(3.5m), Entry(2.25m), Entry(1m, inhouse: true) };
        Assert.Equal(6.75m, TimeCalculationService.EntriesHours(entries));
    }

    [Fact]
    public void OvertimeHours_PositiveOnlyAboveThreshold()
    {
        Assert.Equal(0.5m, TimeCalculationService.OvertimeHours(new TimeOnly(8, 0), new TimeOnly(17, 30), 8m));
        Assert.Equal(0m, TimeCalculationService.OvertimeHours(new TimeOnly(8, 0), new TimeOnly(15, 0), 8m));
    }

    [Fact]
    public void Validate_SumExceedsWorked_IsErrorInStrictMode()
    {
        var entries = new List<TimeEntry> { Entry(5m), Entry(5m) };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(16, 0), entries, ValidationSettings.Defaults, strict: true);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "sum-exceeds" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_SumExceedsWorked_IsWarningInDraftMode()
    {
        var entries = new List<TimeEntry> { Entry(5m), Entry(5m) };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(16, 0), entries, ValidationSettings.Defaults, strict: false);
        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "sum-exceeds" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_EndBeforeStart_AlwaysError()
    {
        var result = TimeCalculationService.Validate(new TimeOnly(16, 0), new TimeOnly(8, 0), new List<TimeEntry>(), ValidationSettings.Defaults, strict: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "time-order");
    }

    [Fact]
    public void Validate_BoatRequiredForNonInhouse()
    {
        var entries = new List<TimeEntry> { Entry(4m, boatId: null) };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(16, 0), entries, ValidationSettings.Defaults, strict: true);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "boat-required");
    }

    [Fact]
    public void Validate_InhouseDoesNotNeedBoatOrDescription()
    {
        var entries = new List<TimeEntry> { Entry(7m, inhouse: true, work: "") };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(16, 0), entries, ValidationSettings.Defaults, strict: true);
        Assert.False(result.HasErrors);
        Assert.DoesNotContain(result.Issues, i => i.Code == "work-empty");
    }

    [Fact]
    public void Validate_BoatWorkRequiresDescriptionOnSubmit()
    {
        var entries = new List<TimeEntry> { Entry(4m, work: "") };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(16, 0), entries, ValidationSettings.Defaults, strict: true);
        Assert.Contains(result.Issues, i => i.Code == "work-empty" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_OvertimeIsWarning_NotError()
    {
        var entries = new List<TimeEntry> { Entry(10m) };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(19, 0), entries, ValidationSettings.Defaults, strict: true);
        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "overtime" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_ZeroHoursIsError()
    {
        var entries = new List<TimeEntry> { Entry(0m) };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(16, 0), entries, ValidationSettings.Defaults, strict: true);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "hours-positive");
    }

    [Fact]
    public void Validate_MaxHoursPerDayExceeded()
    {
        var settings = ValidationSettings.Defaults with { MaxHoursPerDay = 8m };
        var entries = new List<TimeEntry> { Entry(12m) };
        var result = TimeCalculationService.Validate(new TimeOnly(8, 0), new TimeOnly(22, 0), entries, settings, strict: true);
        Assert.Contains(result.Issues, i => i.Code == "max-hours");
    }

    [Fact]
    public void HasOverlap_DetectsIntersectionOnSameDate()
    {
        var others = new List<(DateOnly, TimeOnly, TimeOnly)>
        {
            (new DateOnly(2026, 11, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };
        Assert.True(TimeCalculationService.HasOverlap(new DateOnly(2026, 11, 3), new TimeOnly(12, 0), new TimeOnly(20, 0), others));
        Assert.False(TimeCalculationService.HasOverlap(new DateOnly(2026, 11, 4), new TimeOnly(12, 0), new TimeOnly(20, 0), others));
        Assert.False(TimeCalculationService.HasOverlap(new DateOnly(2026, 11, 3), new TimeOnly(16, 0), new TimeOnly(20, 0), others));
    }
}
