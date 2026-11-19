using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public class TelegramLinkResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ApplicationUser? User { get; set; }
}

public class TelegramLinkService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private readonly ApplicationDbContext _db;

    public TelegramLinkService(ApplicationDbContext db) => _db = db;

    public async Task<string> CreateCodeAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Не указан пользователь.", nameof(userId));
        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            throw new InvalidOperationException("Пользователь не найден.");

        var oldCodes = await _db.TelegramLinkCodes.Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        _db.TelegramLinkCodes.RemoveRange(oldCodes);

        var code = RandomNumberGenerator.GetHexString(32);
        var now = DateTime.Now;
        _db.TelegramLinkCodes.Add(new TelegramLinkCode
        {
            UserId = userId,
            CodeHash = HashCode(code),
            CreatedAt = now,
            ExpiresAt = now.Add(CodeLifetime),
        });
        await _db.SaveChangesAsync(cancellationToken);
        return code;
    }

    public async Task<TelegramLinkResult> LinkAsync(long telegramUserId, string? telegramUsername,
        string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Failure("Укажите код привязки.");

        var codeHash = HashCode(code.Trim());
        var now = DateTime.Now;
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var linkCode = await _db.TelegramLinkCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);
        if (linkCode is null || linkCode.UsedAt is not null || linkCode.ExpiresAt <= now)
            return Failure("Код привязки недействителен или истёк. Создайте новый код в профиле.");

        var linkedAccount = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);
        if (linkedAccount is not null && linkedAccount.Id != linkCode.UserId)
            return Failure("Этот Telegram-аккаунт уже привязан к другому пользователю.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == linkCode.UserId, cancellationToken);
        if (user is null) return Failure("Пользователь не найден.");
        if (user.TelegramUserId is not null && user.TelegramUserId != telegramUserId)
            return Failure("К учётной записи уже привязан другой Telegram-аккаунт. Сначала отвяжите его в профиле.");

        try
        {
            var claimed = await _db.TelegramLinkCodes
                .Where(c => c.Id == linkCode.Id && c.UsedAt == null && c.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), cancellationToken);
            if (claimed != 1) return Failure("Код привязки уже использован. Создайте новый код в профиле.");

            user.TelegramUserId = telegramUserId;
            user.TelegramUsername = string.IsNullOrWhiteSpace(telegramUsername)
                ? null
                : telegramUsername.Trim().TrimStart('@');
            user.TelegramLinkedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TelegramLinkResult { Success = true, User = user };
        }
        catch (DbUpdateException)
        {
            return Failure("Не удалось привязать Telegram-аккаунт. Проверьте, не привязан ли он к другому пользователю.");
        }
    }

    public async Task<ApplicationUser?> GetUserAsync(long telegramUserId,
        CancellationToken cancellationToken = default)
        => await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

    public async Task UnlinkAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return;

        user.TelegramUserId = null;
        user.TelegramUsername = null;
        user.TelegramLinkedAt = null;
        var codes = await _db.TelegramLinkCodes.Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        _db.TelegramLinkCodes.RemoveRange(codes);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string HashCode(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static TelegramLinkResult Failure(string error) => new()
    {
        Success = false,
        Error = error,
    };
}
