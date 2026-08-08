using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

internal static class LiteEmojiResolver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Regex Shortcode = new(@":[A-Za-z0-9][A-Za-z0-9_+\-]{1,96}:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, (string Id, string Url)> YouTube = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> TwitchUrl = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim YtGate = new(1, 1);
    private static string _lastBroadcast = string.Empty;
    private static DateTime _lastYtLoad = DateTime.MinValue;

    static LiteEmojiResolver()
    {
        LoadEmbeddedFallback();
        // Known YouTube spelling variants.
        Alias(":person-turquoise-waving:", ":person-turqouise-waving:");
        Alias(":person-turquoise-waving-speech:", ":person-turquoise-waving-speech:");
    }

    public static async Task EnrichAsync(ChatMessage m, string broadcastId)
    {
        if (m.Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
            await ResolveYouTubeAsync(m, broadcastId).ConfigureAwait(false);
        else if (m.Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase))
            await ResolveTwitchAsync(m).ConfigureAwait(false);
    }

    private static async Task ResolveYouTubeAsync(ChatMessage m, string broadcastId)
    {
        var matches = Shortcode.Matches(m.Text ?? string.Empty).Cast<Match>().ToList();
        if (matches.Count == 0) return;
        if (!string.Equals(_lastBroadcast, broadcastId, StringComparison.Ordinal))
        {
            _lastBroadcast = broadcastId ?? string.Empty;
            _lastYtLoad = DateTime.MinValue;
        }

        var resolved = MakeYouTubeEmotes(m.Text, matches);
        if (resolved.Count < matches.Count && !string.IsNullOrWhiteSpace(broadcastId) && DateTime.UtcNow - _lastYtLoad > TimeSpan.FromMinutes(2))
        {
            await RefreshYouTubeCatalogAsync(broadcastId).ConfigureAwait(false);
            resolved = MakeYouTubeEmotes(m.Text, matches);
        }
        Merge(m, resolved);
    }

    private static List<ChatEmote> MakeYouTubeEmotes(string text, IEnumerable<Match> matches)
    {
        var result = new List<ChatEmote>();
        lock (YouTube)
        {
            foreach (var match in matches)
            {
                if (!YouTube.TryGetValue(match.Value, out var def)) continue;
                result.Add(new ChatEmote { Platform = "YOUTUBE", Id = def.Id, Name = match.Value, Start = match.Index, End = match.Index + match.Length - 1, ImageUrl = def.Url });
            }
        }
        return result;
    }

    private static async Task RefreshYouTubeCatalogAsync(string broadcastId)
    {
        if (!await YtGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (DateTime.UtcNow - _lastYtLoad < TimeSpan.FromMinutes(2)) return;
            _lastYtLoad = DateTime.UtcNow;
            var url = "https://www.youtube.com/live_chat?v=" + Uri.EscapeDataString(broadcastId) + "&is_popout=1&hl=en";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 TiHiY-StreamControl-MINI-Lite/2.0");
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;
            var html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var jsonText = ExtractAssignedJson(html, "ytInitialData");
            if (string.IsNullOrWhiteSpace(jsonText)) return;
            var root = JsonNode.Parse(jsonText);
            if (root is null) return;
            var learned = new Dictionary<string, (string Id, string Url)>(StringComparer.Ordinal);
            Walk(root, learned);
            lock (YouTube) foreach (var p in learned) YouTube[p.Key] = p.Value;
        }
        catch { }
        finally { YtGate.Release(); }
    }

    private static void Walk(JsonNode node, Dictionary<string, (string Id, string Url)> learned)
    {
        if (node is JsonObject obj)
        {
            var id = SafeString(obj["emojiId"]);
            if (!string.IsNullOrWhiteSpace(id) && obj["shortcuts"] is JsonArray shortcuts)
            {
                var url = LargestThumbnail(obj["image"]);
                if (!string.IsNullOrWhiteSpace(url))
                    foreach (var s in shortcuts)
                    {
                        var shortcut = SafeString(s);
                        if (!string.IsNullOrWhiteSpace(shortcut)) learned[shortcut] = (id, url);
                    }
            }
            foreach (var p in obj) if (p.Value is not null) Walk(p.Value, learned);
        }
        else if (node is JsonArray array)
            foreach (var child in array) if (child is not null) Walk(child, learned);
    }

    private static string LargestThumbnail(JsonNode? node)
    {
        if (node is not JsonObject image || image["thumbnails"] is not JsonArray thumbs) return string.Empty;
        string best = string.Empty; var bestWidth = -1;
        foreach (var t in thumbs.OfType<JsonObject>())
        {
            var url = SafeString(t["url"]); var width = SafeInt(t["width"]);
            if (!string.IsNullOrWhiteSpace(url) && width >= bestWidth) { best = url; bestWidth = width; }
        }
        return best;
    }

    private static async Task ResolveTwitchAsync(ChatMessage m)
    {
        if (m.Emotes is null || m.Emotes.Count == 0) return;
        foreach (var emote in m.Emotes)
        {
            if (string.IsNullOrWhiteSpace(emote.Id)) continue; // authoritative GIF URL already provided by Twitch.
            string best;
            lock (TwitchUrl) if (TwitchUrl.TryGetValue(emote.Id, out best!)) { emote.ImageUrl = best; continue; }
            var animated = $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emote.Id)}/animated/dark/2.0";
            var fallback = $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emote.Id)}/static/dark/2.0";
            best = fallback;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, animated);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) best = animated;
            }
            catch { }
            lock (TwitchUrl) TwitchUrl[emote.Id] = best;
            emote.ImageUrl = best;
        }
    }

    private static void Merge(ChatMessage m, IEnumerable<ChatEmote> extra)
    {
        m.Emotes = (m.Emotes ?? new List<ChatEmote>()).Concat(extra)
            .Where(x => x.Start >= 0 && x.End >= x.Start && !string.IsNullOrWhiteSpace(x.ImageUrl))
            .GroupBy(x => $"{x.Start}:{x.End}:{x.ImageUrl}", StringComparer.Ordinal)
            .Select(g => g.First()).OrderBy(x => x.Start).ThenByDescending(x => x.Length).ToList();
    }

    private static void LoadEmbeddedFallback()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("YouTubeGlobalEmojiFallbackCatalog.cs", StringComparison.OrdinalIgnoreCase));
            if (name is null) return;
            using var stream = asm.GetManifestResourceStream(name); if (stream is null) return;
            using var reader = new StreamReader(stream); var source = reader.ReadToEnd();
            foreach (var line in source.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith(':') || !line.Contains('|')) continue;
                var parts = line.Split('|', 3); if (parts.Length < 2) continue;
                var shortcut = parts[0].Trim(); var id = parts[1].Trim();
                var url = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : string.IsNullOrWhiteSpace(id) ? string.Empty : $"https://yt3.ggpht.com/{id}=s48-c";
                if (!string.IsNullOrWhiteSpace(shortcut) && !string.IsNullOrWhiteSpace(url)) YouTube[shortcut] = (id, url);
            }
        }
        catch { }
    }

    private static void Alias(string alias, string original) { lock (YouTube) if (YouTube.TryGetValue(original, out var v)) YouTube[alias] = v; }
    private static string SafeString(JsonNode? node) { try { return node?.GetValue<string>() ?? string.Empty; } catch { return string.Empty; } }
    private static int SafeInt(JsonNode? node) { try { return node?.GetValue<int>() ?? 0; } catch { return int.TryParse(node?.ToString(), out var n) ? n : 0; } }

    private static string ExtractAssignedJson(string html, string marker)
    {
        var from = 0;
        while (from < html.Length)
        {
            var i = html.IndexOf(marker, from, StringComparison.Ordinal); if (i < 0) return string.Empty;
            var brace = html.IndexOf('{', i + marker.Length); if (brace < 0) return string.Empty;
            var result = ExtractBalanced(html, brace); if (!string.IsNullOrWhiteSpace(result)) return result;
            from = i + marker.Length;
        }
        return string.Empty;
    }
    private static string ExtractBalanced(string value, int start)
    {
        var depth=0; var inString=false; var escape=false;
        for (var i=start;i<value.Length;i++)
        {
            var c=value[i];
            if(inString){if(escape){escape=false;continue;} if(c=='\\'){escape=true;continue;} if(c=='"')inString=false;continue;}
            if(c=='"'){inString=true;continue;} if(c=='{')depth++; else if(c=='}' && --depth==0)return value[start..(i+1)];
        }
        return string.Empty;
    }
}
