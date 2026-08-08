using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Enriches the single ChatMessage model used by the MINI main multichat,
/// local game overlay and OBS Browser Source overlay.
///
/// YouTube Data API exposes displayMessage but not rich emoji image URLs, so the
/// resolver learns the current public live-chat emoji catalog without spending
/// extra YouTube Data API quota. Twitch IRC already provides official emote IDs;
/// the resolver upgrades those to the best CDN representation and also resolves
/// official Cheermotes/Bits.
/// </summary>
internal static class PlatformRichContentBridge
{
    private static readonly YouTubeRichContentResolver YouTube = new();
    private static readonly TwitchRichContentResolver Twitch = new();
    private static readonly HashSet<string> Processing = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static bool _hooked;

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_hooked || sender is not MainWindow) return;
        _hooked = true;
        App.Services.Chat.MessageAdded += Chat_MessageAdded;
    }

    private static void Chat_MessageAdded(object? sender, ChatMessage message)
    {
        var platform = message.Platform?.Trim().ToUpperInvariant() ?? string.Empty;
        if (platform is not "YOUTUBE" and not "TWITCH") return;

        var key = string.IsNullOrWhiteSpace(message.ExternalId)
            ? $"{platform}:{message.Time.Ticks}:{message.User}:{message.Text}"
            : $"{platform}:{message.ExternalId}";

        lock (Gate)
        {
            if (!Processing.Add(key)) return;
        }

        _ = EnrichAsync(message, key);
    }

    private static async Task EnrichAsync(ChatMessage message, string key)
    {
        try
        {
            RichChatResult result;
            if (message.Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
            {
                result = await YouTube.ResolveAsync(
                    message,
                    App.Services.YouTube.ActiveBroadcastId,
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                result = await Twitch.ResolveAsync(message, CancellationToken.None).ConfigureAwait(false);
            }

            if (!result.Changed) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                message.Text = result.Text;
                message.Emotes = result.Emotes;
                var messages = App.Services.Chat.Messages;
                for (var i = messages.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(messages[i], message)) continue;
                    messages[i] = message;
                    break;
                }
            });
        }
        catch (Exception ex)
        {
            try { App.Services.Logger.Error("Rich chat content", ex); } catch { }
        }
        finally
        {
            lock (Gate) Processing.Remove(key);
        }
    }
}

internal sealed record RichChatResult(string Text, List<ChatEmote> Emotes, bool Changed);

internal sealed class YouTubeRichContentResolver
{
    private static readonly Regex ShortcutRegex = new(
        @":[A-Za-z0-9][A-Za-z0-9_+\-]{1,96}:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly Dictionary<string, YouTubeEmojiDefinition> _emoji = new(StringComparer.Ordinal);
    private readonly List<YouTubeVisualDefinition> _recentVisuals = new();
    private string _broadcastId = string.Empty;
    private DateTime _lastCatalogUtc = DateTime.MinValue;
    private DateTime _lastForcedRefreshUtc = DateTime.MinValue;

    public YouTubeRichContentResolver()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TiHiY-StreamControl-MINI/1.0");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9,en;q=0.7");

        AddFallback(
            ":hand-pink-waving:",
            "KOxdr_z3A5h1Gb7kqnxqOCnbZrBmxI2B_tRQ453BhTWUhYAlpg5ZP8IKEBkcvRoY8grY91Q",
            "https://yt3.ggpht.com/KOxdr_z3A5h1Gb7kqnxqOCnbZrBmxI2B_tRQ453BhTWUhYAlpg5ZP8IKEBkcvRoY8grY91Q=s48-c");
        AddFallback(
            ":face-blue-smiling:",
            "cktIaPxFwnrPwn-alHvnvedHLUJwbHi8HCK3AgbHpphrMAW99qw0bDfxuZagSY5ieE9BBrA",
            "https://yt3.ggpht.com/cktIaPxFwnrPwn-alHvnvedHLUJwbHi8HCK3AgbHpphrMAW99qw0bDfxuZagSY5ieE9BBrA=s48-c");
        AddFallback(
            ":face-red-droopy-eyes:",
            "oih9s26MOYPWC_uL6tgaeOlXSGBv8MMoDrWzBt-80nEiVSL9nClgnuzUAKqkU9_TWygF6CI",
            "https://yt3.ggpht.com/oih9s26MOYPWC_uL6tgaeOlXSGBv8MMoDrWzBt-80nEiVSL9nClgnuzUAKqkU9_TWygF6CI=s48-c");
        AddFallback(
            ":face-purple-crying:",
            "g6_km98AfdHbN43gvEuNdZ2I07MmzVpArLwEvNBwwPqpZYzszqhRzU_DXALl11TchX5_xFE",
            "https://yt3.ggpht.com/g6_km98AfdHbN43gvEuNdZ2I07MmzVpArLwEvNBwwPqpZYzszqhRzU_DXALl11TchX5_xFE=s48-c");
        AddFallback(
            ":person-turqouise-waving:",
            "uNSzQ2M106OC1L3VGzrOsGNjopboOv-m1bnZKFGuh0DxcceSpYHhYbuyggcgnYyaF3o-AQ",
            "https://yt3.ggpht.com/uNSzQ2M106OC1L3VGzrOsGNjopboOv-m1bnZKFGuh0DxcceSpYHhYbuyggcgnYyaF3o-AQ=s48-c");
    }

    public async Task<RichChatResult> ResolveAsync(ChatMessage message, string broadcastId, CancellationToken token)
    {
        var text = message.Text ?? string.Empty;
        if (!string.Equals(_broadcastId, broadcastId, StringComparison.Ordinal))
        {
            _broadcastId = broadcastId ?? string.Empty;
            _lastCatalogUtc = DateTime.MinValue;
        }

        var matches = ShortcutRegex.Matches(text).Cast<Match>().ToList();
        if (matches.Count > 0 || message.Role.Equals("Donor", StringComparison.OrdinalIgnoreCase))
            await EnsureCatalogAsync(false, token).ConfigureAwait(false);

        var emotes = ResolveKnownShortcuts(text, matches);
        var unresolved = matches.Count > emotes.Count;
        if (unresolved && DateTime.UtcNow - _lastForcedRefreshUtc > TimeSpan.FromSeconds(12))
        {
            _lastForcedRefreshUtc = DateTime.UtcNow;
            await EnsureCatalogAsync(true, token).ConfigureAwait(false);
            emotes = ResolveKnownShortcuts(text, matches);
        }

        if (emotes.Count == 0 && (message.Emotes?.Count ?? 0) == 0 && message.Role.Equals("Donor", StringComparison.OrdinalIgnoreCase))
        {
            var visual = FindVisual(message.User, text);
            if (visual is not null)
            {
                const string tokenText = ":youtube-visual:";
                text = tokenText + " " + text;
                emotes.Add(new ChatEmote
                {
                    Platform = "YOUTUBE",
                    Id = visual.Id,
                    Name = visual.Name,
                    Start = 0,
                    End = tokenText.Length - 1,
                    ImageUrl = visual.Url
                });
            }
        }

        var merged = MergeEmotes(message.Emotes, emotes);
        return new RichChatResult(text, merged, text != message.Text || !SameEmotes(message.Emotes, merged));
    }

    private List<ChatEmote> ResolveKnownShortcuts(string text, IReadOnlyList<Match> matches)
    {
        var result = new List<ChatEmote>();
        lock (_emoji)
        {
            foreach (var match in matches)
            {
                if (!_emoji.TryGetValue(match.Value, out var def)) continue;
                result.Add(new ChatEmote
                {
                    Platform = "YOUTUBE",
                    Id = def.Id,
                    Name = match.Value,
                    Start = match.Index,
                    End = match.Index + match.Length - 1,
                    ImageUrl = def.Url
                });
            }
        }
        return result;
    }

    private async Task EnsureCatalogAsync(bool force, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_broadcastId)) return;
        if (!force && DateTime.UtcNow - _lastCatalogUtc < TimeSpan.FromMinutes(2)) return;
        await _catalogGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!force && DateTime.UtcNow - _lastCatalogUtc < TimeSpan.FromMinutes(2)) return;
            var url = "https://www.youtube.com/live_chat?v=" + Uri.EscapeDataString(_broadcastId) + "&is_popout=1&hl=en";
            using var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            var html = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            ParsePublicLiveChat(html);
            _lastCatalogUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        finally { _catalogGate.Release(); }
    }

    private void ParsePublicLiveChat(string html)
    {
        var jsonText = ExtractAssignedJson(html, "ytInitialData");
        if (string.IsNullOrWhiteSpace(jsonText)) return;
        JsonNode? root;
        try { root = JsonNode.Parse(jsonText); }
        catch { return; }
        if (root is null) return;
        var learned = new Dictionary<string, YouTubeEmojiDefinition>(StringComparer.Ordinal);
        var visuals = new List<YouTubeVisualDefinition>();
        Walk(root, learned, visuals);
        lock (_emoji)
        {
            foreach (var pair in learned) _emoji[pair.Key] = pair.Value;
            _recentVisuals.Clear();
            _recentVisuals.AddRange(visuals.TakeLast(100));
        }
    }

    private static void Walk(JsonNode node, Dictionary<string, YouTubeEmojiDefinition> learned, List<YouTubeVisualDefinition> visuals)
    {
        if (node is JsonObject obj)
        {
            if (TryString(obj["emojiId"], out var emojiId) && obj["shortcuts"] is JsonArray shortcuts)
            {
                var url = LargestThumbnailUrl(obj["image"]);
                if (!string.IsNullOrWhiteSpace(url))
                    foreach (var shortcutNode in shortcuts)
                        if (TryString(shortcutNode, out var shortcut) && !string.IsNullOrWhiteSpace(shortcut))
                            learned[shortcut] = new YouTubeEmojiDefinition(emojiId, url);
            }

            if (obj["liveChatPaidStickerRenderer"] is JsonObject sticker)
            {
                var url = LargestThumbnailUrl(sticker["sticker"]);
                var user = RendererText(sticker["authorName"]);
                var name = RendererText(sticker["purchaseAmountText"]);
                if (!string.IsNullOrWhiteSpace(url))
                    visuals.Add(new YouTubeVisualDefinition("paid-sticker", user, string.IsNullOrWhiteSpace(name) ? "Super Sticker" : name, url));
            }

            if (obj["giftMetadata"] is JsonObject gift)
            {
                var giftUrl = SafeString(gift["giftUrl"]);
                var giftName = SafeString(gift["giftName"]);
                if (!string.IsNullOrWhiteSpace(giftUrl))
                    visuals.Add(new YouTubeVisualDefinition("youtube-gift", string.Empty, string.IsNullOrWhiteSpace(giftName) ? "YouTube Gift" : giftName, giftUrl));
            }

            foreach (var pair in obj)
                if (pair.Value is not null) Walk(pair.Value, learned, visuals);
            return;
        }
        if (node is JsonArray array)
            foreach (var child in array)
                if (child is not null) Walk(child, learned, visuals);
    }

    private YouTubeVisualDefinition? FindVisual(string user, string text)
    {
        lock (_emoji)
            return _recentVisuals.LastOrDefault(v =>
                (!string.IsNullOrWhiteSpace(v.User) && v.User.Equals(user, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(v.Name) && text.Contains(v.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string LargestThumbnailUrl(JsonNode? imageNode)
    {
        if (imageNode is not JsonObject image || image["thumbnails"] is not JsonArray thumbnails) return string.Empty;
        var bestUrl = string.Empty;
        var bestWidth = -1;
        foreach (var item in thumbnails.OfType<JsonObject>())
        {
            var url = SafeString(item["url"]);
            var width = SafeInt(item["width"]);
            if (string.IsNullOrWhiteSpace(url)) continue;
            if (width >= bestWidth) { bestWidth = width; bestUrl = url; }
        }
        return bestUrl;
    }

    private static string RendererText(JsonNode? node)
    {
        if (node is not JsonObject obj) return string.Empty;
        var simple = SafeString(obj["simpleText"]);
        if (!string.IsNullOrWhiteSpace(simple)) return simple;
        return obj["runs"] is JsonArray runs
            ? string.Concat(runs.OfType<JsonObject>().Select(x => SafeString(x["text"]))).Trim()
            : string.Empty;
    }

    private static string ExtractAssignedJson(string html, string marker)
    {
        var searchFrom = 0;
        while (searchFrom < html.Length)
        {
            var markerIndex = html.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (markerIndex < 0) return string.Empty;
            var brace = html.IndexOf('{', markerIndex + marker.Length);
            if (brace < 0) return string.Empty;
            var extracted = ExtractBalancedObject(html, brace);
            if (!string.IsNullOrWhiteSpace(extracted)) return extracted;
            searchFrom = markerIndex + marker.Length;
        }
        return string.Empty;
    }

    private static string ExtractBalancedObject(string value, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < value.Length; i++)
        {
            var ch = value[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (ch == '\\') { escape = true; continue; }
                if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"') { inString = true; continue; }
            if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return value[start..(i + 1)];
        }
        return string.Empty;
    }

    private void AddFallback(string shortcut, string id, string url)
    {
        lock (_emoji) _emoji[shortcut] = new YouTubeEmojiDefinition(id, url);
    }

    private static bool TryString(JsonNode? node, out string value)
    {
        value = SafeString(node);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string SafeString(JsonNode? node)
    {
        try { return node?.GetValue<string>() ?? string.Empty; } catch { return string.Empty; }
    }

    private static int SafeInt(JsonNode? node)
    {
        try { return node?.GetValue<int>() ?? 0; }
        catch
        {
            try { return int.TryParse(node?.ToString(), out var value) ? value : 0; } catch { return 0; }
        }
    }

    private sealed record YouTubeEmojiDefinition(string Id, string Url);
    private sealed record YouTubeVisualDefinition(string Id, string User, string Name, string Url);

    private static List<ChatEmote> MergeEmotes(IEnumerable<ChatEmote>? original, IEnumerable<ChatEmote> extra) =>
        (original ?? Enumerable.Empty<ChatEmote>()).Concat(extra)
            .Where(x => x.Start >= 0 && x.End >= x.Start && !string.IsNullOrWhiteSpace(x.ImageUrl))
            .GroupBy(x => $"{x.Start}:{x.End}:{x.ImageUrl}", StringComparer.Ordinal)
            .Select(g => g.First()).OrderBy(x => x.Start).ThenByDescending(x => x.Length).ToList();

    private static bool SameEmotes(IReadOnlyList<ChatEmote>? left, IReadOnlyList<ChatEmote>? right)
    {
        left ??= Array.Empty<ChatEmote>();
        right ??= Array.Empty<ChatEmote>();
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (left[i].Start != right[i].Start || left[i].End != right[i].End || !string.Equals(left[i].ImageUrl, right[i].ImageUrl, StringComparison.Ordinal)) return false;
        return true;
    }
}

internal sealed class TwitchRichContentResolver
{
    private static readonly Regex CheerRegex = new(
        @"(?<![A-Za-z0-9_])(?<prefix>[A-Za-z][A-Za-z0-9_]*?)(?<bits>[1-9][0-9]*)(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _cheerGate = new(1, 1);
    private readonly Dictionary<string, List<CheerTier>> _cheers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _emoteUrlCache = new(StringComparer.Ordinal);
    private DateTime _lastCheerLoadUtc = DateTime.MinValue;

    public async Task<RichChatResult> ResolveAsync(ChatMessage message, CancellationToken token)
    {
        var text = message.Text ?? string.Empty;
        var emotes = message.Emotes?.Select(Clone).ToList() ?? new List<ChatEmote>();
        var changed = false;

        foreach (var emote in emotes)
        {
            if (string.IsNullOrWhiteSpace(emote.Id)) continue;
            var best = await ResolveOfficialEmoteUrlAsync(emote.Id, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(best) && !string.Equals(best, emote.ImageUrl, StringComparison.Ordinal))
            {
                emote.ImageUrl = best;
                changed = true;
            }
        }

        var cheerMatches = CheerRegex.Matches(text).Cast<Match>().ToList();
        if (cheerMatches.Count > 0)
        {
            await EnsureCheermotesAsync(token).ConfigureAwait(false);
            foreach (var match in cheerMatches)
            {
                if (!int.TryParse(match.Groups["bits"].Value, out var bits)) continue;
                if (!_cheers.TryGetValue(match.Groups["prefix"].Value, out var tiers)) continue;
                var tier = tiers.Where(x => x.MinimumBits <= bits).OrderByDescending(x => x.MinimumBits).FirstOrDefault();
                if (tier is null || string.IsNullOrWhiteSpace(tier.Url)) continue;
                if (emotes.Any(e => e.Start <= match.Index && e.End >= match.Index + match.Length - 1)) continue;
                emotes.Add(new ChatEmote
                {
                    Platform = "TWITCH",
                    Id = tier.Id,
                    Name = match.Value,
                    Start = match.Index,
                    End = match.Index + match.Length - 1,
                    ImageUrl = tier.Url
                });
                changed = true;
            }
        }

        emotes = emotes.OrderBy(x => x.Start).ThenByDescending(x => x.Length).ToList();
        return new RichChatResult(text, emotes, changed);
    }

    private async Task<string> ResolveOfficialEmoteUrlAsync(string id, CancellationToken token)
    {
        lock (_emoteUrlCache)
            if (_emoteUrlCache.TryGetValue(id, out var cached)) return cached;
        var animated = $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(id)}/animated/dark/2.0";
        var fallback = $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(id)}/static/dark/2.0";
        var result = fallback;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, animated);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) result = animated;
        }
        catch { }
        lock (_emoteUrlCache) _emoteUrlCache[id] = result;
        return result;
    }

    private async Task EnsureCheermotesAsync(CancellationToken token)
    {
        if (_cheers.Count > 0 && DateTime.UtcNow - _lastCheerLoadUtc < TimeSpan.FromHours(6)) return;
        await _cheerGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_cheers.Count > 0 && DateTime.UtcNow - _lastCheerLoadUtc < TimeSpan.FromHours(6)) return;
            OAuthToken? tokenData = null;
            try
            {
                var raw = App.Services.Credentials.LoadSecret("TWITCH_TOKEN");
                if (!string.IsNullOrWhiteSpace(raw)) tokenData = JsonSerializer.Deserialize<OAuthToken>(raw);
            }
            catch { }
            var clientId = App.Services.Settings.Value.TwitchClientId?.Trim() ?? string.Empty;
            if (tokenData is null || string.IsNullOrWhiteSpace(tokenData.AccessToken) || string.IsNullOrWhiteSpace(clientId)) return;
            var path = "https://api.twitch.tv/helix/bits/cheermotes";
            var broadcaster = App.Services.Settings.Value.TwitchBroadcasterId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(broadcaster)) path += "?broadcaster_id=" + Uri.EscapeDataString(broadcaster);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            request.Headers.Add("Client-Id", clientId);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var root = JsonNode.Parse(body)?.AsObject();
            if (root?["data"] is not JsonArray data) return;

            _cheers.Clear();
            foreach (var item in data.OfType<JsonObject>())
            {
                var prefix = SafeString(item["prefix"]);
                if (string.IsNullOrWhiteSpace(prefix) || item["tiers"] is not JsonArray tiersArray) continue;
                var tiers = new List<CheerTier>();
                foreach (var tierNode in tiersArray.OfType<JsonObject>())
                {
                    var min = SafeInt(tierNode["min_bits"]);
                    var id = SafeString(tierNode["id"]);
                    var images = tierNode["images"] as JsonObject;
                    var url = FirstNonEmpty(
                        SafeString(images?["dark"]?["animated"]?["2"]),
                        SafeString(images?["dark"]?["static"]?["2"]),
                        SafeString(images?["light"]?["animated"]?["2"]),
                        SafeString(images?["light"]?["static"]?["2"]));
                    if (!string.IsNullOrWhiteSpace(url)) tiers.Add(new CheerTier(id, min, url));
                }
                if (tiers.Count > 0) _cheers[prefix] = tiers;
            }
            _lastCheerLoadUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        finally { _cheerGate.Release(); }
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static ChatEmote Clone(ChatEmote source) => new()
    {
        Platform = source.Platform,
        Id = source.Id,
        Name = source.Name,
        Start = source.Start,
        End = source.End,
        ImageUrl = source.ImageUrl
    };

    private static string SafeString(JsonNode? node)
    {
        try { return node?.GetValue<string>() ?? string.Empty; } catch { return string.Empty; }
    }

    private static int SafeInt(JsonNode? node)
    {
        try { return node?.GetValue<int>() ?? 0; }
        catch { return int.TryParse(node?.ToString(), out var value) ? value : 0; }
    }

    private sealed record CheerTier(string Id, int MinimumBits, string Url);
}
