namespace WorkTimeTracker.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public long? EngineeringChatId { get; set; }
    public string SiteBaseUrl { get; set; } = string.Empty;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(BotToken);
}
