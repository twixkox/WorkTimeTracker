using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TelegramLinkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly TelegramLinkService _service;

    public TelegramLinkServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _service = new TelegramLinkService(_db);
    }

    [Fact]
    public async Task CreateCode_StoresOnlyHashWithTenMinuteLifetime()
    {
        var user = await CreateUserAsync();

        var code = await _service.CreateCodeAsync(user.Id);

        Assert.Equal(32, code.Length);
        var stored = Assert.Single(_db.TelegramLinkCodes);
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal(64, stored.CodeHash.Length);
        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain(code, stored.CodeHash);
        Assert.Null(stored.UsedAt);
        Assert.Equal(TimeSpan.FromMinutes(10), stored.ExpiresAt - stored.CreatedAt);
        Assert.DoesNotContain(code, await _db.TelegramLinkCodes.Select(c => c.CodeHash).ToListAsync());
    }

    [Fact]
    public async Task CreateCodeAgain_RevokesPreviousCode()
    {
        var user = await CreateUserAsync();
        var firstCode = await _service.CreateCodeAsync(user.Id);

        var secondCode = await _service.CreateCodeAsync(user.Id);

        Assert.NotEqual(firstCode, secondCode);
        Assert.Single(_db.TelegramLinkCodes);
        var firstResult = await _service.LinkAsync(10001, "first", firstCode);
        Assert.False(firstResult.Success);
        var secondResult = await _service.LinkAsync(10001, "second", secondCode);
        Assert.True(secondResult.Success, secondResult.Error);
        Assert.Equal(10001, secondResult.User!.TelegramUserId);
    }

    [Fact]
    public async Task Link_ValidCode_SetsTelegramFieldsMarksCodeUsedAndReturnsUser()
    {
        var user = await CreateUserAsync();
        var code = await _service.CreateCodeAsync(user.Id);
        var before = DateTime.Now.AddSeconds(-1);

        var result = await _service.LinkAsync(123456789, "  @engineer  ", code);

        Assert.True(result.Success, result.Error);
        Assert.Same(user, result.User);
        Assert.Equal(123456789, user.TelegramUserId);
        Assert.Equal("engineer", user.TelegramUsername);
        Assert.NotNull(user.TelegramLinkedAt);
        Assert.InRange(user.TelegramLinkedAt!.Value, before, DateTime.Now.AddSeconds(1));
        _db.ChangeTracker.Clear();
        Assert.NotNull((await _db.TelegramLinkCodes.SingleAsync()).UsedAt);
        var linkedUser = await _service.GetUserAsync(123456789);
        Assert.Equal(user.Id, linkedUser!.Id);
    }

    [Fact]
    public async Task Link_CodeCannotBeUsedTwice()
    {
        var user = await CreateUserAsync();
        var code = await _service.CreateCodeAsync(user.Id);
        Assert.True((await _service.LinkAsync(10001, "first", code)).Success);

        var secondResult = await _service.LinkAsync(10002, "second", code);

        Assert.False(secondResult.Success);
        Assert.Equal(10001, user.TelegramUserId);
        Assert.Equal("first", user.TelegramUsername);
        Assert.Single(_db.TelegramLinkCodes);
    }

    [Fact]
    public async Task Link_ExpiredOrUnknownCode_IsRejectedWithoutLinking()
    {
        var user = await CreateUserAsync();
        var code = await _service.CreateCodeAsync(user.Id);
        var stored = await _db.TelegramLinkCodes.SingleAsync();
        stored.ExpiresAt = DateTime.Now.AddSeconds(-1);
        await _db.SaveChangesAsync();

        var expiredResult = await _service.LinkAsync(10001, "expired", code);
        var unknownResult = await _service.LinkAsync(10001, "unknown", "not-a-code");

        Assert.False(expiredResult.Success);
        Assert.False(unknownResult.Success);
        Assert.Null(user.TelegramUserId);
        Assert.Null(stored.UsedAt);
    }

    [Fact]
    public async Task Link_TelegramAlreadyLinkedToAnotherUser_IsRejected()
    {
        var firstUser = await CreateUserAsync();
        var secondUser = await CreateUserAsync();
        var firstCode = await _service.CreateCodeAsync(firstUser.Id);
        Assert.True((await _service.LinkAsync(10001, "first", firstCode)).Success);
        var secondCode = await _service.CreateCodeAsync(secondUser.Id);

        var result = await _service.LinkAsync(10001, "duplicate", secondCode);

        Assert.False(result.Success);
        Assert.Equal(firstUser.Id, (await _service.GetUserAsync(10001))!.Id);
        Assert.Null(secondUser.TelegramUserId);
        Assert.Null((await _db.TelegramLinkCodes.SingleAsync(c => c.UserId == secondUser.Id)).UsedAt);
    }

    [Fact]
    public async Task Link_UserAlreadyLinkedToAnotherTelegram_IsRejected()
    {
        var user = await CreateUserAsync();
        user.TelegramUserId = 10001;
        user.TelegramUsername = "existing";
        user.TelegramLinkedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var code = await _service.CreateCodeAsync(user.Id);

        var result = await _service.LinkAsync(10002, "new", code);

        Assert.False(result.Success);
        Assert.Equal(10001, user.TelegramUserId);
        Assert.Equal("existing", user.TelegramUsername);
        Assert.Null((await _db.TelegramLinkCodes.SingleAsync()).UsedAt);
    }

    [Fact]
    public async Task Unlink_ClearsTelegramFieldsAndRevokesCodes()
    {
        var user = await CreateUserAsync();
        var code = await _service.CreateCodeAsync(user.Id);
        Assert.True((await _service.LinkAsync(10001, "engineer", code)).Success);
        var unusedCode = await _service.CreateCodeAsync(user.Id);

        await _service.UnlinkAsync(user.Id);

        Assert.Null(user.TelegramUserId);
        Assert.Null(user.TelegramUsername);
        Assert.Null(user.TelegramLinkedAt);
        Assert.Empty(_db.TelegramLinkCodes);
        Assert.Null(await _service.GetUserAsync(10001));
        Assert.False((await _service.LinkAsync(10002, "new", unusedCode)).Success);
    }

    [Fact]
    public async Task GetUser_UnknownTelegramOrUnlinkedUser_ReturnsNull()
    {
        var user = await CreateUserAsync();

        Assert.Null(await _service.GetUserAsync(404));
        await _service.UnlinkAsync(user.Id);
        Assert.Null(await _service.GetUserAsync(404));
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = "Тестовый пользователь",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
