using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>
/// Правила переходов статусов табеля.
/// Черновик/Отклонён → На утверждении → Утверждён; На утверждении → Отклонён.
/// </summary>
public static class WorkflowService
{
    public static bool CanEmployeeEdit(TimeSheetStatus status)
        => status is TimeSheetStatus.Draft or TimeSheetStatus.Rejected;

    public static bool CanSubmit(TimeSheetStatus status)
        => status is TimeSheetStatus.Draft or TimeSheetStatus.Rejected;

    public static bool CanApprove(TimeSheetStatus status)
        => status == TimeSheetStatus.Submitted;

    public static bool CanReject(TimeSheetStatus status)
        => status == TimeSheetStatus.Submitted;

    /// <summary>Мастер может редактировать табели сотрудников своего участка на утверждении.</summary>
    public static bool CanMasterEdit(TimeSheetStatus status)
        => status == TimeSheetStatus.Submitted;

    /// <summary>Попытка перехода; возвращает false при недопустимом переходе.</summary>
    public static bool TrySubmit(WorkDay day)
    {
        if (!CanSubmit(day.Status)) return false;
        day.Status = TimeSheetStatus.Submitted;
        day.SubmittedAt = DateTime.Now;
        day.RejectComment = null;
        return true;
    }

    public static bool TryApprove(WorkDay day, string userId, string userName, bool final)
    {
        if (!CanApprove(day.Status)) return false;
        day.Status = TimeSheetStatus.Approved;
        day.ApprovedAt = DateTime.Now;
        day.ApprovedByUserId = userId;
        day.ApprovedByName = userName;
        day.FinalApproved = final;
        day.RejectComment = null;
        return true;
    }

    public static bool TryReject(WorkDay day, string comment)
    {
        if (!CanReject(day.Status)) return false;
        if (string.IsNullOrWhiteSpace(comment)) return false;
        day.Status = TimeSheetStatus.Rejected;
        day.RejectComment = comment.Trim();
        day.FinalApproved = false;
        return true;
    }

    /// <summary>Вернуть утверждённый табель на доработку (админ/начальник производства).</summary>
    public static bool TryReopen(WorkDay day, string comment)
    {
        if (day.Status != TimeSheetStatus.Approved) return false;
        day.Status = TimeSheetStatus.Rejected;
        day.RejectComment = string.IsNullOrWhiteSpace(comment) ? "Возвращён на доработку." : comment.Trim();
        day.FinalApproved = false;
        return true;
    }
}
