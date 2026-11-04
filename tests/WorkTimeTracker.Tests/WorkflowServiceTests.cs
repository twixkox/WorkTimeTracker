using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class WorkflowServiceTests
{
    private static WorkDay Day(TimeSheetStatus status) => new() { Status = status };

    [Fact]
    public void EmployeeCanEdit_OnlyDraftAndRejected()
    {
        Assert.True(WorkflowService.CanEmployeeEdit(TimeSheetStatus.Draft));
        Assert.True(WorkflowService.CanEmployeeEdit(TimeSheetStatus.Rejected));
        Assert.False(WorkflowService.CanEmployeeEdit(TimeSheetStatus.Submitted));
        Assert.False(WorkflowService.CanEmployeeEdit(TimeSheetStatus.Approved));
    }

    [Fact]
    public void MasterCanEdit_OnlySubmitted()
    {
        Assert.True(WorkflowService.CanMasterEdit(TimeSheetStatus.Submitted));
        Assert.False(WorkflowService.CanMasterEdit(TimeSheetStatus.Draft));
        Assert.False(WorkflowService.CanMasterEdit(TimeSheetStatus.Approved));
    }

    [Fact]
    public void Submit_FromDraftOrRejected_SetsSubmitted()
    {
        var day = Day(TimeSheetStatus.Draft);
        Assert.True(WorkflowService.TrySubmit(day));
        Assert.Equal(TimeSheetStatus.Submitted, day.Status);
        Assert.NotNull(day.SubmittedAt);
    }

    [Fact]
    public void Submit_FromApproved_Fails()
    {
        var day = Day(TimeSheetStatus.Approved);
        Assert.False(WorkflowService.TrySubmit(day));
        Assert.Equal(TimeSheetStatus.Approved, day.Status);
    }

    [Fact]
    public void Approve_FromSubmitted_SetsApproved()
    {
        var day = Day(TimeSheetStatus.Submitted);
        Assert.True(WorkflowService.TryApprove(day, "user1", "Мастер", final: false));
        Assert.Equal(TimeSheetStatus.Approved, day.Status);
        Assert.Equal("Мастер", day.ApprovedByName);
        Assert.False(day.FinalApproved);
    }

    [Fact]
    public void Reject_RequiresComment()
    {
        var day = Day(TimeSheetStatus.Submitted);
        Assert.False(WorkflowService.TryReject(day, "  "));
        Assert.Equal(TimeSheetStatus.Submitted, day.Status);

        Assert.True(WorkflowService.TryReject(day, "Неверная сумма часов"));
        Assert.Equal(TimeSheetStatus.Rejected, day.Status);
        Assert.Equal("Неверная сумма часов", day.RejectComment);
    }

    [Fact]
    public void RejectedDay_CanBeResubmitted_AndCommentCleared()
    {
        var day = Day(TimeSheetStatus.Rejected);
        day.RejectComment = "ошибка";
        Assert.True(WorkflowService.TrySubmit(day));
        Assert.Equal(TimeSheetStatus.Submitted, day.Status);
        Assert.Null(day.RejectComment);
    }

    [Fact]
    public void Reopen_ApprovedToRejected()
    {
        var day = Day(TimeSheetStatus.Approved);
        Assert.True(WorkflowService.TryReopen(day, "уточнить"));
        Assert.Equal(TimeSheetStatus.Rejected, day.Status);
        Assert.False(day.FinalApproved);
    }

    [Fact]
    public void Reopen_FromSubmitted_Fails()
    {
        var day = Day(TimeSheetStatus.Submitted);
        Assert.False(WorkflowService.TryReopen(day, "уточнить"));
    }
}
