using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

public sealed class DiscordNotificationService : IDisposable
{
    private readonly AppSettingsAccessor _settings;
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentials;
    private readonly AppLogger _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public DiscordServerProfileService Profiles { get; }

    public DiscordNotificationService(SettingsService settingsService, AppSettingsAccessor settings, CredentialService credentials, AppLogger logger)
        : this(settings, settingsService, credentials, logger)
    {
    }

    public DiscordNotificationService(AppSettingsAccessor settings, SettingsService settingsService, CredentialService credentials, AppLogger logger)
    {
        _settings = settings;
        _settingsService = settingsService;
        _credentials = credentials;
        _logger = logger;
        Profiles = new DiscordServerProfileService(settingsService.Folder);
    }

    public void SaveBotToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Введіть токен Discord-бота.");
        _credentials.SaveSecret("DISCORD_BOT_TOKEN", token.Trim());
    }

    public string LoadBotToken() => _credentials.LoadSecret("DISCORD_BOT_TOKEN");
    public bool HasBotToken => !string.IsNullOrWhiteSpace(LoadBotToken());
    public void ForgetBotToken() => _credentials.DeleteSecret("DISCORD_BOT_TOKEN");

    public async Task TestAsync(CancellationToken token = default)
    {
        var info = new StreamLiveInfo
        {
            Platform = "Twitch",
            IsLive = true,
            BroadcastId = "test",
            Title = "ТЕСТ: TiHiY StreamControl MINI",
            Url = "https://www.twitch.tv/tihiy_ded",
            Viewers = 123,
            Likes = 0
        };

        var profiles = Profiles.Profiles.Where(x => x.Enabled && x.StreamChannelIds.Count > 0).ToList();
        if (profiles.Count > 0)
        {
            foreach (var profile in profiles)
                await SendLiveToProfileAsync(profile, info, token, test: true).ConfigureAwait(false);
            return;
        }

        var payload = BuildPayload(
            "✅ TiHiY Stream Notify Bot підключено",
            "Тестове повідомлення про трансляцію успішно надіслано з TiHiY StreamControl MINI.",
            info.Url,
            0x2BEB82,
            string.Empty,
            "TiHiY Stream Notify Bot • канал трансляцій",
            _settings.Value.DiscordMention);
        await SendPayloadToChannelsAsync(payload, _settings.Value.DiscordChannelIds, token).ConfigureAwait(false);
    }

    public async Task TestProfileStreamsAsync(string serverId, CancellationToken token = default)
    {
        var profile = Profiles.Find(serverId) ?? throw new InvalidOperationException("Профіль Discord-сервера не знайдено.");
        if (profile.StreamChannelIds.Count == 0) throw new InvalidOperationException("Для цього сервера не вибрано каналів стрімів.");
        await SendLiveToProfileAsync(profile, new StreamLiveInfo
        {
            Platform = "Twitch",
            IsLive = true,
            BroadcastId = "test",
            Title = "ТЕСТ: персональне повідомлення сервера",
            Url = "https://www.twitch.tv/tihiy_ded",
            Viewers = 123
        }, token, test: true).ConfigureAwait(false);
    }

    public async Task TestMonetizationAsync(CancellationToken token = default)
    {
        var test = CreateTestDonation();
        var profiles = Profiles.Profiles.Where(x => x.Enabled && x.MonetizationChannelIds.Count > 0).ToList();
        if (profiles.Count > 0)
        {
            foreach (var profile in profiles)
                await SendMonetizationToProfileAsync(profile, test, token, test: true).ConfigureAwait(false);
            return;
        }
        await NotifyMonetizationAsync(test, token, force: true).ConfigureAwait(false);
    }

    public async Task TestProfileMonetizationAsync(string serverId, CancellationToken token = default)
    {
        var profile = Profiles.Find(serverId) ?? throw new InvalidOperationException("Профіль Discord-сервера не знайдено.");
        if (profile.MonetizationChannelIds.Count == 0) throw new InvalidOperationException("Для цього сервера не вибрано каналів донатів.");
        await SendMonetizationToProfileAsync(profile, CreateTestDonation(), token, test: true).ConfigureAwait(false);
    }

    public async Task NotifyLiveAsync(StreamLiveInfo info, CancellationToken token = default)
    {
        if (!_settings.Value.DiscordNotificationsEnabled || !info.IsLive) return;
        if (info.Platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase) && !_settings.Value.DiscordNotifyTwitch) return;
        if (info.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && !_settings.Value.DiscordNotifyYouTube) return;

        var profiles = Profiles.Profiles.Where(x => x.Enabled && x.StreamChannelIds.Count > 0).ToList();
        if (profiles.Count > 0)
        {
            foreach (var profile in profiles)
                await SendLiveToProfileAsync(profile, info, token).ConfigureAwait(false);
        }
        else
        {
            var payload = BuildLivePayload(
                info,
                _settings.Value.DiscordMention,
                _settings.Value.DiscordMessageTemplate,
                string.Empty,
                false);
            await SendPayloadToChannelsAsync(payload, _settings.Value.DiscordChannelIds, token).ConfigureAwait(false);
        }

        if (info.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            _settings.Value.YouTubeLastNotifiedBroadcastId = info.BroadcastId;
        if (info.Platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            _settings.Value.TwitchLastStreamId = info.BroadcastId;
        _settingsService.Save(_settings.Value);
    }

    public async Task NotifyMonetizationAsync(DonationEvent donation, CancellationToken token = default, bool force = false)
    {
        var settings = _settings.Value;
        if (!force && !settings.DiscordMonetizationEnabled) return;
        if (!force && donation.Source.Contains("DONATELLO", StringComparison.OrdinalIgnoreCase) && (!settings.DiscordNotifyDonatelloMonetization || !settings.DonatelloNotifyDiscord)) return;
        if (!force && donation.Source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase) && !settings.DiscordNotifyTwitchMonetization) return;
        if (!force && donation.Source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase) && !settings.DiscordNotifyYouTubeMonetization) return;
        if (!force && donation.Source.Equals("TEST", StringComparison.OrdinalIgnoreCase)) return;

        var profiles = Profiles.Profiles.Where(x => x.Enabled && x.MonetizationChannelIds.Count > 0).ToList();
        if (profiles.Count > 0)
        {
            foreach (var profile in profiles)
                await SendMonetizationToProfileAsync(profile, donation, token, test: force).ConfigureAwait(false);
            return;
        }

        var payload = BuildMonetizationPayload(
            donation,
            settings.DiscordMonetizationMention,
            string.Empty,
            string.Empty,
            force);
        await SendPayloadToChannelsAsync(payload, settings.DiscordMonetizationChannelIds, token).ConfigureAwait(false);
    }

    private async Task SendLiveToProfileAsync(DiscordServerProfile profile, StreamLiveInfo info, CancellationToken token, bool test = false)
    {
        var payload = BuildLivePayload(info, profile.StreamMention, profile.StreamTemplate, profile.ServerName, test);
        await SendPayloadToChannelsAsync(payload, string.Join(Environment.NewLine, profile.StreamChannelIds), token).ConfigureAwait(false);
    }

    private async Task SendMonetizationToProfileAsync(DiscordServerProfile profile, DonationEvent donation, CancellationToken token, bool test = false)
    {
        var payload = BuildMonetizationPayload(donation, profile.MonetizationMention, profile.MonetizationTemplate, profile.ServerName, test);
        await SendPayloadToChannelsAsync(payload, string.Join(Environment.NewLine, profile.MonetizationChannelIds), token).ConfigureAwait(false);
    }

    private JsonObject BuildLivePayload(StreamLiveInfo info, string mention, string template, string serverName, bool test)
    {
        var platformIcon = info.Platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase) ? "🟣" : "🔴";
        var title = test
            ? $"✅ Тест профілю Discord • {DisplayServer(serverName)}"
            : $"{platformIcon} TiHiY-DED уже в ефірі на {info.Platform}!";
        var actualTemplate = string.IsNullOrWhiteSpace(template)
            ? "🔴 {platform}: трансляція почалася!\n{title}\n{url}"
            : template;
        var description = ReplaceStreamTemplate(actualTemplate, info, serverName);
        if (info.Viewers > 0 && !actualTemplate.Contains("{viewers}", StringComparison.OrdinalIgnoreCase))
            description += $"\nГлядачів зараз: **{info.Viewers}**";
        if (info.Likes > 0 && !actualTemplate.Contains("{likes}", StringComparison.OrdinalIgnoreCase))
            description += $" • Лайків: **{info.Likes}**";

        return BuildPayload(
            title,
            description,
            info.Url,
            test ? 0x2BEB82 : info.Platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase) ? 0x9146FF : 0xFF0000,
            test ? string.Empty : info.ThumbnailUrl,
            string.IsNullOrWhiteSpace(serverName)
                ? "TiHiY Stream Notify Bot • повідомлення про початок трансляції"
                : $"TiHiY Stream Notify Bot • {serverName}",
            mention);
    }

    private JsonObject BuildMonetizationPayload(DonationEvent donation, string mention, string template, string serverName, bool test)
    {
        var icon = donation.Kind.Equals("SUBSCRIPTION", StringComparison.OrdinalIgnoreCase) ? "⭐" : "💛";
        var kind = donation.Kind.Equals("SUBSCRIPTION", StringComparison.OrdinalIgnoreCase) ? "платна підписка" : "донат";
        var title = test
            ? $"✅ Тест монетизації • {DisplayServer(serverName)}"
            : $"{icon} Новий {kind}: {donation.DisplayAmount}";

        var description = string.IsNullOrWhiteSpace(template)
            ? $"**{donation.User}** • {donation.Source}" + (string.IsNullOrWhiteSpace(donation.Message) ? string.Empty : $"\n{donation.Message}")
            : ReplaceMonetizationTemplate(template, donation, serverName, kind);

        var color = test ? 0x2BEB82
            : donation.Source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase) ? 0x9146FF
            : donation.Source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase) ? 0xFF0000
            : 0xFFD329;
        var eventUrl = donation.Source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase)
            ? "https://www.twitch.tv/tihiy_ded"
            : donation.Source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase)
                ? "https://www.youtube.com/@TiHiY-DED"
                : _settings.Value.DonatelloPageUrl;

        return BuildPayload(
            title,
            description,
            eventUrl,
            color,
            string.Empty,
            string.IsNullOrWhiteSpace(serverName)
                ? "TiHiY Stream Notify Bot • донати та платні підписки"
                : $"TiHiY Stream Notify Bot • {serverName}",
            mention);
    }

    private static string ReplaceStreamTemplate(string template, StreamLiveInfo info, string serverName) => template
        .Replace("{platform}", info.Platform, StringComparison.OrdinalIgnoreCase)
        .Replace("{title}", string.IsNullOrWhiteSpace(info.Title) ? "TiHiY-DED LIVE" : info.Title, StringComparison.OrdinalIgnoreCase)
        .Replace("{url}", info.Url, StringComparison.OrdinalIgnoreCase)
        .Replace("{viewers}", info.Viewers.ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{likes}", info.Likes.ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{server}", serverName, StringComparison.OrdinalIgnoreCase);

    private string ReplaceMonetizationTemplate(string template, DonationEvent donation, string serverName, string kind)
    {
        var eventUrl = donation.Source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase)
            ? "https://www.twitch.tv/tihiy_ded"
            : donation.Source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase)
                ? "https://www.youtube.com/@TiHiY-DED"
                : _settings.Value.DonatelloPageUrl;
        return template
            .Replace("{user}", donation.User, StringComparison.OrdinalIgnoreCase)
            .Replace("{amount}", donation.Amount.ToString("0.##"), StringComparison.OrdinalIgnoreCase)
            .Replace("{currency}", donation.Currency, StringComparison.OrdinalIgnoreCase)
            .Replace("{source}", donation.Source, StringComparison.OrdinalIgnoreCase)
            .Replace("{message}", donation.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{kind}", kind, StringComparison.OrdinalIgnoreCase)
            .Replace("{url}", eventUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("{server}", serverName, StringComparison.OrdinalIgnoreCase);
    }

    private static DonationEvent CreateTestDonation() => new()
    {
        Source = "DONATELLO",
        Kind = "SUBSCRIPTION",
        User = "Тестовий підписник",
        Amount = 100,
        Currency = "UAH",
        Message = "Перевірка персонального повідомлення сервера."
    };

    private static string DisplayServer(string name) => string.IsNullOrWhiteSpace(name) ? "Discord" : name;

    private static JsonObject BuildPayload(string title, string description, string url, int color, string imageUrl, string footer, string mention)
    {
        var embed = new JsonObject
        {
            ["title"] = title,
            ["description"] = description,
            ["color"] = color,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["footer"] = new JsonObject { ["text"] = footer }
        };
        if (!string.IsNullOrWhiteSpace(url)) embed["url"] = url;
        if (!string.IsNullOrWhiteSpace(imageUrl)) embed["image"] = new JsonObject { ["url"] = imageUrl };

        var mentionTypes = new JsonArray();
        mentionTypes.Add("everyone");
        mentionTypes.Add("roles");
        return new JsonObject
        {
            ["content"] = string.IsNullOrWhiteSpace(mention) ? null : mention.Trim(),
            ["allowed_mentions"] = new JsonObject { ["parse"] = mentionTypes },
            ["embeds"] = new JsonArray(embed)
        };
    }

    private async Task SendPayloadToChannelsAsync(JsonObject payload, string channelIdText, CancellationToken token)
    {
        var botToken = LoadBotToken();
        if (string.IsNullOrWhiteSpace(botToken)) throw new InvalidOperationException("Токен Discord-бота не збережено.");
        var channelIds = ParseChannelIds(channelIdText);
        if (channelIds.Count == 0) throw new InvalidOperationException("Не вказано ID потрібних текстових каналів Discord.");

        var errors = new List<string>();
        foreach (var channelId in channelIds)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v10/channels/{Uri.EscapeDataString(channelId)}/messages");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {TrimBody(body)}");
                _logger.Info($"Discord: повідомлення надіслано в канал {channelId}");
            }
            catch (Exception ex)
            {
                errors.Add($"{channelId}: {ex.GetBaseException().Message}");
                _logger.Error($"Discord канал {channelId}", ex);
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Не всі Discord-канали прийняли повідомлення:\n" + string.Join("\n", errors));
    }

    private static List<string> ParseChannelIds(string text) => text
        .Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string TrimBody(string body)
    {
        var value = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= 300 ? value : value[..300] + "…";
    }

    public void Dispose() => _http.Dispose();
}
