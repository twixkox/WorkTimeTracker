using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class EngineeringTaskAttachmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly string _contentRoot;
    private readonly string _storagePath;

    public EngineeringTaskAttachmentServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        _contentRoot = Path.Combine(Path.GetTempPath(), "WorkTimeTracker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
        _storagePath = Path.Combine(_contentRoot, "App_Data", "engineering-attachments");
    }

    [Theory]
    [InlineData(AppRoles.ProductionHead)]
    [InlineData(AppRoles.ChiefEngineer)]
    [InlineData(AppRoles.Engineer)]
    [InlineData(AppRoles.Admin)]
    public async Task SaveAsync_EngineeringRole_StoresRandomFileMetadataAndAudit(string role)
    {
        var task = await SeedTaskAsync();
        var actor = Actor(role);
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var service = CreateService();

        var result = await service.SaveAsync(actor, task.Id, "  Чертёж.pdf  ", "application/pdf",
            new MemoryStream(content), content.Length, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(task.Id, result.Task?.Id);
        var attachment = await _db.EngineeringTaskAttachments.SingleAsync();
        Assert.Equal(task.Id, attachment.EngineeringTaskId);
        Assert.Equal("Чертёж.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(content.Length, attachment.Size);
        Assert.Equal(actor.UserId, attachment.UploadedByUserId);
        Assert.Equal(actor.UserName, attachment.UploadedByName);
        Assert.NotEqual("Чертёж.pdf", attachment.StoredFileName);
        Assert.DoesNotContain("..", attachment.StoredFileName);
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_storagePath, attachment.StoredFileName)));

        var audit = await _db.AuditLogs.SingleAsync();
        Assert.Equal(actor.UserId, audit.UserId);
        Assert.Equal(actor.UserName, audit.UserName);
        Assert.Equal("AttachmentAdded", audit.Action);
        Assert.Equal("EngineeringTask", audit.EntityName);
        Assert.Equal(task.Id.ToString(), audit.EntityId);
        Assert.Contains("Чертёж.pdf", audit.Details);
        Assert.Contains(content.Length.ToString(), audit.Details);
    }

    [Theory]
    [InlineData(AppRoles.Employee)]
    [InlineData(AppRoles.SectionMaster)]
    public async Task SaveAndOpen_NonEngineeringRole_AreDenied(string role)
    {
        var task = await SeedTaskAsync();
        var service = CreateService();
        var content = new byte[] { 1, 2, 3 };

        var denied = await service.SaveAsync(Actor(role), task.Id, "file.txt", "text/plain",
            new MemoryStream(content), content.Length, CancellationToken.None);

        Assert.False(denied.Success);
        Assert.NotNull(denied.Error);
        Assert.Empty(_db.EngineeringTaskAttachments);
        Assert.Empty(_db.AuditLogs);
        AssertStorageEmpty();

        var saved = await service.SaveAsync(Actor(AppRoles.Engineer), task.Id, "file.txt", "text/plain",
            new MemoryStream(content), content.Length, CancellationToken.None);
        Assert.True(saved.Success, saved.Error);
        var attachmentId = await _db.EngineeringTaskAttachments.Select(a => a.Id).SingleAsync();

        Assert.Null(await service.OpenAsync(Actor(role), attachmentId));
    }

    [Fact]
    public async Task SaveAsync_MissingTask_IsRejectedWithoutWritingFile()
    {
        var service = CreateService();
        var content = new byte[] { 1 };

        var result = await service.SaveAsync(Actor(AppRoles.Admin), 12345, "file.txt", "text/plain",
            new MemoryStream(content), content.Length, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("taskId", result.Field);
        Assert.NotNull(result.Error);
        AssertStorageEmpty();
        Assert.Empty(_db.AuditLogs);
    }

    [Theory]
    [InlineData(null, "text/plain", 1L)]
    [InlineData(" ", "text/plain", 1L)]
    [InlineData("..", "text/plain", 1L)]
    [InlineData("../secret.txt", "text/plain", 1L)]
    [InlineData("folder\\secret.txt", "text/plain", 1L)]
    [InlineData("bad\nname.txt", "text/plain", 1L)]
    [InlineData("file.txt", "", 1L)]
    [InlineData("file.txt", "not-a-content-type", 1L)]
    [InlineData("file.txt", "text/plain\r\nX-Test: invalid", 1L)]
    [InlineData("file.txt", "text/plain", 0L)]
    [InlineData("file.txt", "text/plain", -1L)]
    [InlineData("file.txt", "text/plain", 20971521L)]
    public async Task SaveAsync_InvalidNameTypeOrSize_IsRejected(string? fileName, string? contentType, long size)
    {
        var task = await SeedTaskAsync();
        var service = CreateService();

        var result = await service.SaveAsync(Actor(AppRoles.ChiefEngineer), task.Id, fileName!, contentType!,
            new MemoryStream(new byte[] { 1 }), size, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("file", result.Field);
        Assert.NotNull(result.Error);
        Assert.Empty(_db.EngineeringTaskAttachments);
        Assert.Empty(_db.AuditLogs);
        AssertStorageEmpty();
    }

    [Fact]
    public async Task SaveAsync_TooLongFileName_IsRejected()
    {
        var task = await SeedTaskAsync();
        var service = CreateService();

        var result = await service.SaveAsync(Actor(AppRoles.ChiefEngineer), task.Id,
            new string('ф', 256), "text/plain", new MemoryStream(new byte[] { 1 }), 1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("file", result.Field);
        AssertStorageEmpty();
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(4, 3)]
    public async Task SaveAsync_ActualSizeDiffersFromDeclared_DeletesPartialFile(int actualSize, long declaredSize)
    {
        var task = await SeedTaskAsync();
        var service = CreateService();
        var content = new byte[actualSize];

        var result = await service.SaveAsync(Actor(AppRoles.Engineer), task.Id, "file.bin",
            "application/octet-stream", new MemoryStream(content), declaredSize, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("file", result.Field);
        Assert.NotNull(result.Error);
        Assert.Empty(_db.EngineeringTaskAttachments);
        Assert.Empty(_db.AuditLogs);
        AssertStorageEmpty();
    }

    [Fact]
    public async Task SaveAsync_SameOriginalName_CreatesDistinctStorageNames()
    {
        var task = await SeedTaskAsync();
        var service = CreateService();

        var first = await SaveAsync(service, task.Id, "drawing.txt");
        var second = await SaveAsync(service, task.Id, "drawing.txt");

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        var storedNames = await _db.EngineeringTaskAttachments.Select(a => a.StoredFileName).ToListAsync();
        Assert.Equal(2, storedNames.Count);
        Assert.Equal(2, storedNames.Distinct().Count());
        Assert.All(storedNames, name => Assert.Equal(name, Path.GetFileName(name)));
    }

    [Fact]
    public async Task OpenAsync_ExistingAttachment_ReturnsOriginalNameTypeAndContent()
    {
        var task = await SeedTaskAsync();
        var service = CreateService();
        var content = new byte[] { 10, 20, 30, 40 };
        var saved = await service.SaveAsync(Actor(AppRoles.ProductionHead), task.Id, "Схема.bin",
            "application/octet-stream", new MemoryStream(content), content.Length, CancellationToken.None);
        Assert.True(saved.Success, saved.Error);
        var attachmentId = await _db.EngineeringTaskAttachments.Select(a => a.Id).SingleAsync();

        var opened = await service.OpenAsync(Actor(AppRoles.Engineer), attachmentId);

        Assert.NotNull(opened);
        Assert.Equal("Схема.bin", opened.FileName);
        Assert.Equal("application/octet-stream", opened.ContentType);
        await using (opened.Content)
        {
            using var copy = new MemoryStream();
            await opened.Content.CopyToAsync(copy);
            Assert.Equal(content, copy.ToArray());
        }

        Assert.Null(await service.OpenAsync(Actor(AppRoles.Engineer), attachmentId + 1));
    }

    [Fact]
    public async Task OpenAsync_TamperedStoragePath_DoesNotOpenFileOutsideStorage()
    {
        var task = await SeedTaskAsync();
        var service = CreateService();
        Assert.True((await SaveAsync(service, task.Id, "safe.txt")).Success);
        var attachment = await _db.EngineeringTaskAttachments.SingleAsync();
        var outsidePath = Path.GetFullPath(Path.Combine(_storagePath, "..", "outside.txt"));
        await File.WriteAllTextAsync(outsidePath, "секрет");
        attachment.StoredFileName = "../outside.txt";
        await _db.SaveChangesAsync();

        var opened = await service.OpenAsync(Actor(AppRoles.Admin), attachment.Id);

        Assert.Null(opened);
    }

    private EngineeringTaskAttachmentService CreateService()
        => new(_db, new AuditService(_db), new TestHostEnvironment(_contentRoot));

    private async Task<EngineeringTaskOperationResult> SaveAsync(EngineeringTaskAttachmentService service,
        int taskId, string fileName)
    {
        var content = new byte[] { 1, 2, 3 };
        return await service.SaveAsync(Actor(AppRoles.ChiefEngineer), taskId, fileName, "text/plain",
            new MemoryStream(content), content.Length, CancellationToken.None);
    }

    private async Task<EngineeringTask> SeedTaskAsync()
    {
        var task = new EngineeringTask
        {
            CreatedByUserId = "creator",
            CreatedByName = "Главный инженер",
            Title = "Проверка вложений",
        };
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    private static EngineeringTaskActor Actor(string role)
        => new("attachment-user", "Инженер Иванов", new[] { role });

    private void AssertStorageEmpty()
        => Assert.True(!Directory.Exists(_storagePath) || !Directory.EnumerateFileSystemEntries(_storagePath).Any());

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "WorkTimeTracker.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
