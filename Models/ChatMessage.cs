using System.Text.RegularExpressions;

namespace TiHiY.StreamControlCenter.Models;

public sealed class ChatMessage
{
    private static readonly Regex UrlRegex = new(
        @"(?<url>https?://[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string _text = string.Empty;

    public DateTime Time { get; set; } = DateTime.Now;
    public string Platform { get; set; } = "LOCAL";
    public string User { get; set; } = "TiHiY-DED";
    public string Text
    {
        get => NormalizeStreamlabsEventText(_text);
        set => _text = value ?? string.Empty;
    }
    public string DisplayText => CompactUrls(Text);
    public string Role { get; set; } = "Viewer";
    public string ExternalId { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string Foreground { get; set; } = "#EDF7FF";
    public string Background { get; set; } = "Transparent";
    public bool IsHighlighted { get; set; }
    public List<ChatEmote> Emotes { get; set; } = new();
    public bool HasEmotes => Emotes.Count > 0;
    public string DisplayTime => Time.ToString("HH:mm:ss");
    public string PlatformIconPath => Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase)
        ? "/TiHiY.StreamControlCenter;component/Assets/Platforms/twitch.png"
        : Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase)
            ? "/TiHiY.StreamControlCenter;component/Assets/Platforms/youtube.png"
            : Platform.Equals("DONATELLO", StringComparison.OrdinalIgnoreCase)
                ? "/TiHiY.StreamControlCenter;component/Assets/Platforms/donatello.png"
                : Platform.Equals("DISCORD", StringComparison.OrdinalIgnoreCase)
                    ? "/TiHiY.StreamControlCenter;component/Assets/Platforms/discord.png"
                    : "Assets/AppIcon.png";
    public string PlatformColor => Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase)
        ? "#A970FF"
        : Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase)
            ? "#FF3B3B"
            : Platform.Equals("DONATELLO", StringComparison.OrdinalIgnoreCase) ? "#FFD329" : "#46D8FF";

    private string NormalizeStreamlabsEventText(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            !ExternalId.StartsWith("event-chat:streamlabs:", StringComparison.OrdinalIgnoreCase))
            return value;

        var text = value;
        if (text.StartsWith("⭐ ", StringComparison.Ordinal)) text = text["⭐ ".Length..];
        else if (text.StartsWith("💛 ", StringComparison.Ordinal)) text = text["💛 ".Length..];

        var separator = text.IndexOf(" • ", StringComparison.Ordinal);
        if (separator >= 0)
            return separator + 3 < text.Length ? text[(separator + 3)..] : string.Empty;

        // Some Streamlabs events (for example a plain subscription/follow) have no user message.
        // In that case keep only the raw event value/type instead of inventing a sentence.
        return text;
    }

    private static string CompactUrls(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("http", StringComparison.OrdinalIgnoreCase))
            return value;

        return UrlRegex.Replace(value, match =>
        {
            var original = match.Groups["url"].Value;
            var trimmed = original.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
            var suffix = original[trimmed.Length..];

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return original;

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;

            return host + suffix;
        });
    }
}
