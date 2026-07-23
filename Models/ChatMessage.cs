using System.Text.RegularExpressions;

namespace TiHiY.StreamControlCenter.Models;

public sealed class ChatMessage
{
    private static readonly Regex UrlRegex = new(
        @"(?<url>https?://[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DateTime Time { get; set; } = DateTime.Now;
    public string Platform { get; set; } = "LOCAL";
    public string User { get; set; } = "TiHiY-DED";
    public string Text { get; set; } = string.Empty;
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
