using System.Globalization;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Telegram;

public sealed class TelegramNotificationSender : ITelegramNotificationSender
{
    private readonly ITelegramBotApi _api;
    private readonly TelegramOptions _options;
    private readonly NotificationPreferenceService? _preferences;
    private readonly TimeProvider _timeProvider;

    public TelegramNotificationSender(ITelegramBotApi api, IOptions<TelegramOptions> options,
        NotificationPreferenceService? preferences = null, TimeProvider? timeProvider = null)
    {
        _api = api;
        _options = options.Value;
        _preferences = preferences;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SendAsync(ApplicationUser user, string title, string message, string? link,
        CancellationToken cancellationToken = default,
        NotificationCategory category = NotificationCategory.System)
    {
        if (!_options.IsEnabled || !_api.IsEnabled || user.TelegramUserId is not { } telegramUserId)
            return;

        var preferences = _preferences is null
            ? new NotificationPreferences()
            : await _preferences.GetAsync(user.Id, cancellationToken);
        var localTime = TimeOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (!preferences.AllowsTelegram(category, localTime))
            return;

        var text = $"{title}\n{message}";
        var absoluteLink = TelegramText.AbsoluteUrl(_options.SiteBaseUrl, link);
        if (absoluteLink is not null)
            text += $"\n{absoluteLink}";

        await _api.SendMessageAsync(telegramUserId, text, cancellationToken: cancellationToken);
    }
}

public static class TelegramText
{
    public static string? AbsoluteUrl(string? siteBaseUrl, string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
            return absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps
                ? absolute.ToString()
                : null;
        if (!Uri.TryCreate(siteBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            return null;

        var normalizedBase = new Uri(baseUri.ToString().TrimEnd('/') + "/");
        return new Uri(normalizedBase, link.TrimStart('/')).ToString();
    }

    public static string? TaskUrl(string? siteBaseUrl, int taskId)
        => AbsoluteUrl(siteBaseUrl, $"/Engineering/Details/{taskId}");

    public static bool TryParseOptionalDate(string? text, out DateOnly? date)
    {
        date = null;
        if (IsSkip(text))
            return true;
        if (!DateOnly.TryParseExact(text?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;
        date = parsed;
        return true;
    }

    public static bool IsSkip(string? text)
        => string.IsNullOrWhiteSpace(text)
           || text.Trim() is "-" or "—"
           || text.Trim().Equals("пропустить", StringComparison.OrdinalIgnoreCase)
           || text.Trim().Equals("нет", StringComparison.OrdinalIgnoreCase);

    public static string CommandName(string text)
    {
        var command = text.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        var mentionIndex = command.IndexOf('@');
        return (mentionIndex >= 0 ? command[..mentionIndex] : command).ToLowerInvariant();
    }

    public static string Status(EngineeringTaskStatus status) => status switch
    {
        EngineeringTaskStatus.Assigned => "Назначена",
        EngineeringTaskStatus.InProgress => "В работе",
        EngineeringTaskStatus.Done => "Выполнена",
        _ => "Новая",
    };

    public static string Priority(EngineeringTaskPriority priority) => priority switch
    {
        EngineeringTaskPriority.Low => "Низкий",
        EngineeringTaskPriority.High => "Высокий",
        EngineeringTaskPriority.Urgent => "Срочный",
        _ => "Обычный",
    };

    public static string FormatDate(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "не задан";
}
