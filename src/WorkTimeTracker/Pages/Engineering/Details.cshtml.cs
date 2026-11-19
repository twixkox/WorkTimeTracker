using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Engineering;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly EngineeringTaskService _tasks;
    private readonly EngineeringTaskAttachmentService _attachments;

    public DetailsModel(ApplicationDbContext db, CurrentUserService currentUser,
        EngineeringTaskService tasks, EngineeringTaskAttachmentService attachments)
    {
        _db = db;
        _currentUser = currentUser;
        _tasks = tasks;
        _attachments = attachments;
    }

    public EngineeringTask Item { get; private set; } = default!;
    public bool CanEdit => _currentUser.IsInRole(AppRoles.ChiefEngineer) || _currentUser.IsInRole(AppRoles.Admin);
    public bool IsCurrentEngineer => Item.AssigneeId == _currentUser.UserId
        && _currentUser.IsInRole(AppRoles.Engineer);
    public bool CanStart => IsCurrentEngineer && Item.Status == EngineeringTaskStatus.Assigned;
    public bool CanComplete => IsCurrentEngineer && Item.Status == EngineeringTaskStatus.InProgress;
    public bool CanSetPlannedDue => IsCurrentEngineer
        && Item.Status is EngineeringTaskStatus.Assigned or EngineeringTaskStatus.InProgress;

    [BindProperty]
    [StringLength(4000, ErrorMessage = "Комментарий не может быть длиннее 4000 символов.")]
    public string CommentText { get; set; } = string.Empty;

    [BindProperty]
    public IFormFile? Attachment { get; set; }

    [BindProperty]
    public DateOnly? PlannedDueOn { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!HasEngineeringAccess()) return Forbid();
        if (!await LoadTaskAsync(id)) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(int id, CancellationToken cancellationToken)
    {
        if (!HasEngineeringAccess()) return Forbid();
        var actor = CreateActor();
        if (actor is null) return Forbid();

        var result = await _tasks.StartAsync(actor, id, cancellationToken);
        SetOperationMessage(result, "Задача взята в работу.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!HasEngineeringAccess()) return Forbid();
        var actor = CreateActor();
        if (actor is null) return Forbid();

        var result = await _tasks.CompleteAsync(actor, id, cancellationToken);
        SetOperationMessage(result, "Задача отмечена как выполненная.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetPlannedDueAsync(int id, CancellationToken cancellationToken)
    {
        if (!HasEngineeringAccess()) return Forbid();
        var actor = CreateActor();
        if (actor is null) return Forbid();

        if (!ModelState.IsValid)
        {
            if (!await LoadTaskAsync(id)) return NotFound();
            return Page();
        }

        var result = await _tasks.SetPlannedDueAsync(actor, id, PlannedDueOn, cancellationToken);
        SetOperationMessage(result, "Плановый срок сохранён.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddCommentAsync(int id, CancellationToken cancellationToken)
    {
        if (!HasEngineeringAccess()) return Forbid();
        var actor = CreateActor();
        if (actor is null) return Forbid();
        if (!ModelState.IsValid)
        {
            if (!await LoadTaskAsync(id)) return NotFound();
            return Page();
        }

        var result = await _tasks.AddCommentAsync(actor, id, CommentText, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(result.Field is null ? string.Empty : nameof(CommentText),
                result.Error ?? "Не удалось добавить комментарий.");
            if (!await LoadTaskAsync(id)) return NotFound();
            return Page();
        }

        TempData["Success"] = "Комментарий добавлен.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddAttachmentAsync(int id, CancellationToken cancellationToken)
    {
        if (!HasEngineeringAccess()) return Forbid();
        var actor = CreateActor();
        if (actor is null) return Forbid();
        if (Attachment is null)
        {
            ModelState.AddModelError(nameof(Attachment), "Выберите файл.");
            if (!await LoadTaskAsync(id)) return NotFound();
            return Page();
        }

        await using var content = Attachment.OpenReadStream();
        var result = await _attachments.SaveAsync(actor, id, Attachment.FileName,
            Attachment.ContentType, content, Attachment.Length, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(result.Field is null ? string.Empty : nameof(Attachment),
                result.Error ?? "Не удалось загрузить файл.");
            if (!await LoadTaskAsync(id)) return NotFound();
            return Page();
        }

        TempData["Success"] = "Файл добавлен.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetDownloadAsync(int attachmentId, CancellationToken cancellationToken)
    {
        if (!HasEngineeringAccess()) return Forbid();
        var actor = CreateActor();
        if (actor is null) return Forbid();

        var content = await _attachments.OpenAsync(actor, attachmentId, cancellationToken);
        if (content is null) return NotFound();
        return File(content.Content, content.ContentType, content.FileName);
    }

    public static string FormatSize(long size) => size switch
    {
        >= 1024 * 1024 => $"{size / 1024d / 1024d:0.##} МБ",
        >= 1024 => $"{size / 1024d:0.##} КБ",
        _ => $"{size} Б",
    };

    private bool HasEngineeringAccess()
        => _currentUser.IsInRole(AppRoles.ChiefEngineer)
            || _currentUser.IsInRole(AppRoles.Engineer)
            || _currentUser.IsInRole(AppRoles.ProductionHead)
            || _currentUser.IsInRole(AppRoles.Admin);

    private EngineeringTaskActor? CreateActor()
    {
        var userId = _currentUser.UserId;
        if (userId is null) return null;
        var roles = _currentUser.User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
            ?? Array.Empty<string>();
        return new EngineeringTaskActor(userId, _currentUser.UserName, roles);
    }

    private async Task<bool> LoadTaskAsync(int id)
    {
        var item = await _db.EngineeringTasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.Boat)
            .Include(t => t.Comments)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (item is null) return false;
        Item = item;
        PlannedDueOn = item.PlannedDueOn;
        return true;
    }

    private void SetOperationMessage(EngineeringTaskOperationResult result, string success)
    {
        if (result.Success) TempData["Success"] = success;
        else TempData["Error"] = result.Error ?? "Не удалось изменить задачу.";
    }
}
