using System.Net.Mime;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public class EngineeringTaskAttachmentService
{
    public const long MaxFileSize = 20 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly string _storagePath;

    public EngineeringTaskAttachmentService(ApplicationDbContext db, AuditService audit,
        IHostEnvironment environment)
    {
        _db = db;
        _audit = audit;
        _storagePath = Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            "App_Data", "engineering-attachments"));
    }

    public async Task<EngineeringTaskOperationResult> SaveAsync(EngineeringTaskActor actor, int taskId,
        string fileName, string contentType, Stream content, long size, CancellationToken cancellationToken = default)
    {
        if (!HasEngineeringAccess(actor))
            return Failure("Недостаточно прав для добавления вложений.");

        var task = await _db.EngineeringTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return Failure("Задача не найдена.", "taskId");

        var validationError = ValidateInput(fileName, contentType, content, size);
        if (validationError is not null)
            return Failure(validationError, "file");

        var normalizedFileName = fileName.Trim();
        var normalizedContentType = contentType.Trim();
        var storedFileName = $"{Guid.NewGuid():N}.bin";
        var filePath = Path.Combine(_storagePath, storedFileName);
        Directory.CreateDirectory(_storagePath);

        try
        {
            await WriteContentAsync(content, filePath, size, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            TryDelete(filePath);
            return Failure(exception.Message, "file");
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }

        var attachment = new EngineeringTaskAttachment
        {
            EngineeringTaskId = task.Id,
            FileName = normalizedFileName,
            StoredFileName = storedFileName,
            ContentType = normalizedContentType,
            Size = size,
            CreatedAt = DateTime.Now,
            UploadedByUserId = actor.UserId,
            UploadedByName = actor.UserName,
        };

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            _db.EngineeringTaskAttachments.Add(attachment);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync(actor.UserId, actor.UserName, "AttachmentAdded", "EngineeringTask",
                task.Id.ToString(), $"{attachment.FileName}; размер: {attachment.Size} байт");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }

        return new EngineeringTaskOperationResult
        {
            Success = true,
            Task = task,
            Attachment = attachment,
        };
    }

    public async Task<EngineeringTaskAttachmentContent?> OpenAsync(EngineeringTaskActor actor, int attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (!HasEngineeringAccess(actor)) return null;

        var attachment = await _db.EngineeringTaskAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null || !TryGetStoragePath(attachment.StoredFileName, out var filePath)) return null;
        if (!File.Exists(filePath)) return null;

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return new EngineeringTaskAttachmentContent
        {
            Content = stream,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
        };
    }

    private static bool HasEngineeringAccess(EngineeringTaskActor actor)
        => actor.IsInRole(AppRoles.ChiefEngineer)
           || actor.IsInRole(AppRoles.Engineer)
           || actor.IsInRole(AppRoles.ProductionHead)
           || actor.IsInRole(AppRoles.Admin);

    private static string? ValidateInput(string fileName, string contentType, Stream content, long size)
    {
        if (content is null) return "Выберите файл.";
        if (size <= 0) return "Файл не должен быть пустым.";
        if (size > MaxFileSize) return "Размер файла не может превышать 20 МиБ.";
        if (string.IsNullOrWhiteSpace(fileName)) return "Укажите имя файла.";

        var normalizedFileName = fileName.Trim();
        if (normalizedFileName.Length > 255) return "Имя файла не может быть длиннее 255 символов.";
        if (normalizedFileName is "." or ".."
            || normalizedFileName.IndexOfAny(new[] { '/', '\\' }) >= 0
            || normalizedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || normalizedFileName.Any(char.IsControl))
            return "Имя файла содержит недопустимые символы.";

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 255
            || contentType.Any(char.IsControl) || !IsValidContentType(contentType.Trim()))
            return "Укажите корректный тип файла.";

        return null;
    }

    private static bool IsValidContentType(string contentType)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(new ContentType(contentType).MediaType);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task WriteContentAsync(Stream content, string filePath, long size,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var bytesToRead = (int)Math.Min(buffer.Length, size - total + 1);
            var read = await content.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
            if (read == 0) break;

            total += read;
            if (total > size)
                throw new InvalidDataException("Фактический размер файла превышает заявленный.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != size)
            throw new InvalidDataException("Фактический размер файла не совпадает с заявленным.");
    }

    private bool TryGetStoragePath(string storedFileName, out string filePath)
    {
        filePath = string.Empty;
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(_storagePath, storedFileName));
            var storagePrefix = _storagePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!candidate.StartsWith(storagePrefix, comparison)) return false;

            filePath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static EngineeringTaskOperationResult Failure(string error, string? field = null)
        => new()
        {
            Success = false,
            Error = error,
            Field = field,
        };
}

public class EngineeringTaskAttachmentContent
{
    public Stream Content { get; set; } = Stream.Null;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}
