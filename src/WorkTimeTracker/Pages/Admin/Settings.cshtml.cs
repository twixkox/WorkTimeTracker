using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Admin;

public class SettingsModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly CurrentUserService _currentUser;

    public SettingsModel(SettingsService settings, AuditService audit, CurrentUserService currentUser)
    {
        _settings = settings;
        _audit = audit;
        _currentUser = currentUser;
    }

    [BindProperty] public decimal MaxHoursPerDay { get; set; }
    [BindProperty] public decimal OvertimeThreshold { get; set; }
    [BindProperty] public bool TwoStageApproval { get; set; }
    [BindProperty] public string ShiftStart { get; set; } = "09:00";
    [BindProperty] public string ShiftEnd { get; set; } = "18:00";
    [BindProperty] public bool CheckIntervalOverlap { get; set; }

    public async Task OnGet()
    {
        var v = await _settings.GetValidationSettingsAsync();
        MaxHoursPerDay = v.MaxHoursPerDay;
        OvertimeThreshold = v.OvertimeThreshold;
        CheckIntervalOverlap = v.CheckIntervalOverlap;
        TwoStageApproval = await _settings.IsTwoStageApprovalAsync();
        var shift = await _settings.GetShiftTemplateAsync();
        if (shift is not null)
        {
            ShiftStart = shift.Value.Start.ToString("HH\\:mm");
            ShiftEnd = shift.Value.End.ToString("HH\\:mm");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _settings.SetAsync(SettingKeys.MaxHoursPerDay, MaxHoursPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await _settings.SetAsync(SettingKeys.OvertimeThreshold, OvertimeThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await _settings.SetAsync(SettingKeys.TwoStageApproval, TwoStageApproval.ToString());
        await _settings.SetAsync(SettingKeys.ShiftStart, ShiftStart);
        await _settings.SetAsync(SettingKeys.ShiftEnd, ShiftEnd);
        await _settings.SetAsync(SettingKeys.CheckIntervalOverlap, CheckIntervalOverlap.ToString());
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "SystemSettings", null,
            $"MaxHours={MaxHoursPerDay}, Overtime={OvertimeThreshold}, TwoStage={TwoStageApproval}, Shift={ShiftStart}-{ShiftEnd}");
        TempData["Success"] = "Настройки сохранены.";
        return RedirectToPage();
    }
}
