using System.Collections.Concurrent;

namespace WorkTimeTracker.Telegram;

public enum TelegramConversationStep
{
    NewTaskTitle,
    NewTaskDeadline,
    NewTaskHullNumber,
    PlannedDue,
    Comment,
    Attachment,
}

public sealed record TelegramConversation(
    TelegramConversationStep Step,
    int? TaskId = null,
    string? Title = null,
    DateOnly? Deadline = null);

public sealed class TelegramConversationStore
{
    private static readonly TimeSpan ConversationLifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<(long ChatId, long UserId), Entry> _entries = new();

    public void Set(long chatId, long userId, TelegramConversation conversation)
        => _entries[(chatId, userId)] = new Entry(conversation, DateTimeOffset.UtcNow);

    public bool TryGet(long chatId, long userId, out TelegramConversation conversation)
    {
        if (_entries.TryGetValue((chatId, userId), out var entry)
            && DateTimeOffset.UtcNow - entry.UpdatedAt <= ConversationLifetime)
        {
            conversation = entry.Conversation;
            return true;
        }

        Remove(chatId, userId);
        conversation = null!;
        return false;
    }

    public void Remove(long chatId, long userId) => _entries.TryRemove((chatId, userId), out _);

    private sealed record Entry(TelegramConversation Conversation, DateTimeOffset UpdatedAt);
}
