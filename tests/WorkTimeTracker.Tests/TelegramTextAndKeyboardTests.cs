using Telegram.Bot.Types.ReplyMarkups;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using WorkTimeTracker.Telegram;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TelegramTextAndKeyboardTests
{
    [Theory]
    [InlineData("19.11.2026", true)]
    [InlineData("пропустить", true)]
    [InlineData("-", true)]
    [InlineData("2026-11-19", false)]
    [InlineData("31.02.2026", false)]
    public void TryParseOptionalDate_AcceptsOnlyRussianDateOrSkip(string text, bool expected)
    {
        var result = TelegramText.TryParseOptionalDate(text, out var date);

        Assert.Equal(expected, result);
        if (text == "19.11.2026") Assert.Equal(new DateOnly(2026, 11, 19), date);
        if (text is "пропустить" or "-") Assert.Null(date);
    }

    [Fact]
    public void AbsoluteUrl_CombinesBaseWithApplicationPath()
    {
        Assert.Equal("https://example.test/app/Engineering/Details/7",
            TelegramText.TaskUrl("https://example.test/app/", 7));
        Assert.Null(TelegramText.TaskUrl(string.Empty, 7));
    }

    [Fact]
    public void AbsoluteUrl_RejectsNonHttpSchemes()
    {
        Assert.Null(TelegramText.AbsoluteUrl("https://example.test", "javascript:alert(1)"));
        Assert.Equal("https://example.test/app/Engineering/Details/7",
            TelegramText.TaskUrl("https://example.test/app", 7));
    }

    [Fact]
    public void CommandName_RemovesBotMentionAndArguments()
    {
        Assert.Equal("/tasks", TelegramText.CommandName("/Tasks@WorkTimeBot аргумент"));
    }

    [Fact]
    public void TaskDetails_ForCompletedTask_HasNoLifecycleOrPlannedDueActions()
    {
        var task = new EngineeringTask
        {
            Id = 7,
            Status = EngineeringTaskStatus.Done,
            AssigneeId = "engineer",
        };
        var actor = new EngineeringTaskActor("engineer", "Инженер", new[] { AppRoles.Engineer });

        var keyboard = TelegramKeyboards.TaskDetails(task, actor, "https://example.test");

        var callbacks = keyboard.InlineKeyboard.SelectMany(row => row).Select(button => button.CallbackData).ToArray();
        Assert.DoesNotContain("start:7", callbacks);
        Assert.DoesNotContain("complete:7", callbacks);
        Assert.DoesNotContain("plan:7", callbacks);
        Assert.DoesNotContain("reopen:7", callbacks);
        Assert.Contains("comment:7", callbacks);
        Assert.Contains("attachment:7", callbacks);
    }

    [Fact]
    public void TaskDetails_ForAssignedCurrentEngineer_HasStartAndPlannedDueActions()
    {
        var task = new EngineeringTask
        {
            Id = 7,
            Status = EngineeringTaskStatus.Assigned,
            AssigneeId = "engineer",
        };
        var actor = new EngineeringTaskActor("engineer", "Инженер", new[] { AppRoles.Engineer });

        var keyboard = TelegramKeyboards.TaskDetails(task, actor, null);

        var callbacks = keyboard.InlineKeyboard.SelectMany(row => row).Select(button => button.CallbackData).ToArray();
        Assert.Contains("start:7", callbacks);
        Assert.Contains("plan:7", callbacks);
        Assert.DoesNotContain("complete:7", callbacks);
    }
}
