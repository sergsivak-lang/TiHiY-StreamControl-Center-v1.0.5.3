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
    public bool CanSendInThreads { get; init; }
    public bool CanCreatePosts { get; init; }
    public bool CanEmbedLinks { get; init; }
    public bool CanMentionEveryone { get; init; }

    public bool IsForumLike => ChannelType is 15 or 16;
    public bool IsNotificationChannel => ChannelType is 0 or 5 or 15 or 16;

    public bool IsUsable => IsNotificationChannel && CanView && CanEmbedLinks &&
        (IsForumLike
            ? CanCreatePosts && CanSendInThreads
            : CanSend);

    public string DisplayChannel
    {
        get
        {
            var prefix = IsForumLike ? "🗂 " : "#";
            var value = $"{prefix}{ChannelName}";
            return string.IsNullOrWhiteSpace(CategoryName)
                ? value
                : $"{CategoryName} / {value}";
        }
    }

    public string PermissionSummary
    {
        get
        {
            if (!IsNotificationChannel) return "НЕПІДТРИМУВАНИЙ ТИП";
            if (IsUsable)
            {
                var kind = IsForumLike ? "FORUM/MEDIA • ГОТОВО" : "ГОТОВО";
                return CanMentionEveryone ? kind + " • @everyone" : kind;
            }

            return $"БРАК ПРАВ: {string.Join(", ", MissingPermissions())}";
        }
    }

    private IEnumerable<string> MissingPermissions()
    {
        if (!CanView) yield return "View Channel";

        if (IsForumLike)
        {
            if (!CanCreatePosts) yield return "Create Posts";
            if (!CanSendInThreads) yield return "Send Messages in Threads";
        }
        else if (!CanSend)
        {
            yield return "Send Messages";
        }

        if (!CanEmbedLinks) yield return "Embed Links";
    }
}
