using WorkTimeTracker.Pages.Employee;
using Xunit;

namespace WorkTimeTracker.Tests;

public class DayFormModelTests
{
    [Fact]
    public void EffectiveEntries_DropsFullyEmptyRows()
    {
        var form = new DayFormModel
        {
            Entries = new List<EntryInput>
            {
                new() { BoatId = 1, Hours = 4m, WorkDescription = "Сборка" },
                new(), // пустая строка — игнорируется
                new() { IsInhouse = true, Hours = 2m, WorkDescription = "Уборка цеха" },
            },
        };
        var result = form.EffectiveEntries();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void EffectiveEntries_InhouseClearsBoat()
    {
        var form = new DayFormModel
        {
            Entries = new List<EntryInput>
            {
                new() { BoatId = 5, IsInhouse = true, Hours = 3m },
            },
        };
        var result = form.EffectiveEntries();
        Assert.Single(result);
        Assert.Null(result[0].BoatId);
    }

    [Fact]
    public void EffectiveEntries_TrimsDescriptions()
    {
        var form = new DayFormModel
        {
            Entries = new List<EntryInput>
            {
                new() { Hours = 1m, WorkDescription = "  Покраска  " },
            },
        };
        Assert.Equal("Покраска", form.EffectiveEntries()[0].WorkDescription);
    }
}
