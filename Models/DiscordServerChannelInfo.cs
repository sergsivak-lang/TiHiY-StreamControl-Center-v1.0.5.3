namespace TiHiY.StreamControlCenter.Models;

public sealed class DiscordServerChannelInfo
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string ChannelId { get; init; } = string.Empty;
    public string ChannelName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public int ChannelType { get; init; }
    public int Position { get; init; }
    public bool CanView { get; init; }
    public bool CanSend { get; init; }
    public bool CanEmbedLinks { get; init; }
    public bool CanMentionEveryone { get; init; }

    public bool IsNotificationChannel => ChannelType is 0 or 5;
    public bool IsUsable => IsNotificationChannel && CanView && CanSend && CanEmbedLinks;
    public string DisplayChannel => string.IsNullOrWhiteSpace(CategoryName)
        ? $"#{ChannelName}"
        : $"{CategoryName} / #{ChannelName}";
    public string PermissionSummary => !IsNotificationChannel
        ? "НЕ ТЕКСТОВИЙ"
        : IsUsable
            ? CanMentionEveryone ? "ГОТОВО • @everyone" : "ГОТОВО"
            : $"БРАК ПРАВ: {string.Join(", ", MissingPermissions())}";

    private IEnumerable<string> MissingPermissions()
    {
        if (!CanView) yield return "View Channel";
        if (!CanSend) yield return "Send Messages";
        if (!CanEmbedLinks) yield return "Embed Links";
    }
}
