using System.Globalization;
using System.Text.Json;
using TiHiY.StreamControlCenter.Models;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public sealed class LiteCoreService : IAsyncDisposable
{
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _recentDonorChat = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly Lazy<AimpNowPlayingService> _aimp = new(() => new AimpNowPlayingService());
    private int _disposeState;

    public SettingsService SettingsService { get; } = new();
    public AppSettingsAccessor Settings { get; } = new();
    public LitePreferencesStore Preferences { get; } = new();
    public CredentialService Credentials { get; } = new();
    public AppLogger Logger { get; } = new();
    public TwitchService Twitch { get; }
    public YouTubeService YouTube { get; }
    public DonatelloService Donatello { get; }
    public DiscordNotificationService Discord { get; }
    public StreamNotificationBotService NotifyBot { get; }
    public StreamlabsService Streamlabs { get; }
    public LiteStats Stats { get; } = new();
    public ObservableCollection<ChatMessage> Chat { get; } = new();
    public ObservableCollection<LiteEvent> Events { get; } = new();
    public ObservableCollection<DonationEvent> Donations { get; } = new();

    public event EventHandler? StatusChanged;
    public event EventHandler<ChatMessage>? ChatAdded;
    public event EventHandler<LiteEvent>? EventAdded;

    public LiteCoreService()
    {
        Settings.Value = SettingsService.Load();
        // Hard-disable legacy background functionality in the shared settings file.
        Settings.Value.AutoConnectObs = false;
        Settings.Value.Aida64MonitoringEnabled = false;
        Settings.Value.InterfaceAnimationsEnabled = false;
        SettingsService.Save(Settings.Value);

        Twitch = new TwitchService(Settings, SettingsService, Credentials, Logger);
        YouTube = new YouTubeService(Settings, SettingsService, Credentials, Logger);
        Donatello = new DonatelloService(Settings, SettingsService, Credentials, Logger);
        Discord = new DiscordNotificationService(Settings, SettingsService, Credentials, Logger);
        NotifyBot = new StreamNotificationBotService(Settings, SettingsService, Credentials, Twitch, YouTube, Discord, Logger);
        Streamlabs = new StreamlabsService(Preferences, Credentials, Logger);

        Twitch.MessageReceived += (_, m) => _ = AddChatAsync(m);
        YouTube.MessageReceived += (_, m) => _ = AddChatAsync(m);
        Twitch.DonationReceived += (_, d) => HandleMoney(d);
        YouTube.DonationReceived += (_, d) => HandleMoney(d);
        Donatello.DonationReceived += (_, d) => HandleMoney(d);
        Streamlabs.EventReceived += (_, e) => HandleStreamlabs(e);

        Twitch.StatsChanged += (_, _) => UpdateStats();
        YouTube.StatsChanged += (_, _) => UpdateStats();
        Twitch.StatusChanged += (_, _) => RaiseStatus();
        YouTube.StatusChanged += (_, _) => RaiseStatus();
        Donatello.StatusChanged += (_, _) => RaiseStatus();
        Streamlabs.StatusChanged += (_, _) => RaiseStatus();
        NotifyBot.StatusChanged += (_, _) => RaiseStatus();
    }

    public async Task InitializeAsync(bool ciMode = false)
    {
        if (ciMode) return;

        if (Settings.Value.TwitchAutoConnect && Twitch.IsAuthorized)
            _ = SafeAsync(() => Twitch.ConnectAsync(), "Twitch автопідключення");
        if (Settings.Value.YouTubeAutoConnect && YouTube.IsAuthorized)
            _ = SafeAsync(() => YouTube.ConnectAsync(), "YouTube автопідключення");
        if (Preferences.Value.StreamlabsAutoConnect && Streamlabs.IsAuthorized)
            _ = SafeAsync(() => Streamlabs.ConnectAsync(), "Streamlabs автопідключення");
        if (Settings.Value.DonatelloEnabled && Donatello.HasApiToken)
            _ = SafeAsync(() => Donatello.StartAsync(), "Donatello автопідключення");
        if (Settings.Value.NotificationBotAutoStart && Settings.Value.DiscordNotificationsEnabled)
            _ = SafeAsync(() => NotifyBot.StartAsync(), "Discord Notify Bot");
        if (Preferences.Value.HudShowAimp)
            _ = SafeAsync(() => _aimp.Value.InitializeAsync(), "AIMP metadata");

        UpdateStats();
    }

    private async Task AddChatAsync(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.ExternalId) && !_messageIds.Add(message.ExternalId)) return;
        try { await LiteEmojiResolver.EnrichAsync(message, YouTube.ActiveBroadcastId).ConfigureAwait(false); }
        catch (Exception ex) { Logger.Error("Emoji resolver", ex); }

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            Chat.Add(message);
            while (Chat.Count > 220) Chat.RemoveAt(0);
            if (message.Role.Equals("Donor", StringComparison.OrdinalIgnoreCase) || message.Role.Equals("Subscriber", StringComparison.OrdinalIgnoreCase))
                _recentDonorChat[$"{NormalizePlatform(message.Platform)}:{message.User}".ToLowerInvariant()] = DateTime.UtcNow;
            ChatAdded?.Invoke(this, message);
        }));
    }

    private void HandleMoney(DonationEvent donation)
    {
        if (donation.IsHistorical)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => AddDonationOnly(donation)));
            return;
        }

        var platform = NormalizePlatform(donation.Source);
        var kind = NormalizeMoneyKind(donation);
        var fingerprint = Fingerprint(platform, kind, donation.User, donation.Amount, donation.Currency);
        if (RecentlySeen(fingerprint, TimeSpan.FromSeconds(25))) return;

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            AddDonationOnly(donation);
            AddEvent(new LiteEvent
            {
                Platform = platform,
                Type = kind,
                User = donation.User,
                Text = donation.Kind.Equals("SUBSCRIPTION", StringComparison.OrdinalIgnoreCase) ? donation.Message : $"{donation.DisplayAmount} • {donation.Message}",
                Accent = donation.Accent,
                ExternalId = donation.StableId
            });
            EnsureMoneyInChat(platform, kind, donation.User, donation.DisplayAmount, donation.Message, donation.StableId);
        }));

        _ = SafeAsync(() => Discord.NotifyMonetizationAsync(donation), "Discord monetization");
    }

    private void AddDonationOnly(DonationEvent d)
    {
        Donations.Insert(0, d);
        while (Donations.Count > 60) Donations.RemoveAt(Donations.Count - 1);
    }

    private void HandleStreamlabs(StreamlabsEvent e)
    {
        try
        {
            var platform = e.For.Contains("twitch", StringComparison.OrdinalIgnoreCase) ? "TWITCH"
                : e.For.Contains("youtube", StringComparison.OrdinalIgnoreCase) ? "YOUTUBE"
                : "STREAMLABS";
            var type = NormalizeStreamlabsType(e.Type);
            var user = First(e.Payload, "name", "display_name", "username", "from", "gifter") ?? "Глядач";
            var text = First(e.Payload, "message", "comment", "plan", "tier", "months") ?? string.Empty;
            var amount = ReadDecimal(e.Payload, "amount");
            var currency = First(e.Payload, "currency") ?? (type == "BITS" ? "BITS" : string.Empty);
            var semantic = Fingerprint(platform, type, user, amount, currency);
            if (RecentlySeen(semantic, TimeSpan.FromSeconds(25))) return;

            var ext = !string.IsNullOrWhiteSpace(e.EventId) ? "streamlabs:" + e.EventId : "streamlabs:" + Guid.NewGuid().ToString("N");
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                AddEvent(new LiteEvent
                {
                    Platform = platform,
                    Type = type,
                    User = user,
                    Text = BuildStreamlabsText(type, text, amount, currency),
                    Accent = platform == "TWITCH" ? "#A970FF" : platform == "YOUTUBE" ? "#FF4B4B" : "#43CDFF",
                    ExternalId = ext
                });

                if (type is "SUB" or "MEMBER" or "GIFT SUB" or "BITS" or "SUPER CHAT" or "DONATION")
                    EnsureMoneyInChat(platform, type, user, FormatAmount(amount, currency), text, ext);

                if (type is "DONATION" or "BITS" or "SUPER CHAT")
                {
                    var d = new DonationEvent
                    {
                        ExternalId = ext,
                        Source = platform + " " + type,
                        Kind = type is "BITS" or "SUPER CHAT" or "DONATION" ? "DONATION" : "SUBSCRIPTION",
                        User = user,
                        Amount = amount,
                        Currency = currency,
                        Message = text,
                        Accent = platform == "TWITCH" ? "#A970FF" : platform == "YOUTUBE" ? "#FF4B4B" : "#43CDFF"
                    };
                    AddDonationOnly(d);
                }
            }));
        }
        catch (Exception ex) { Logger.Error("Streamlabs event", ex); }
    }

    private void EnsureMoneyInChat(string platform, string kind, string user, string amount, string message, string id)
    {
        var donorKey = $"{platform}:{user}".ToLowerInvariant();
        if (_recentDonorChat.TryGetValue(donorKey, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(15)) return;
        _recentDonorChat[donorKey] = DateTime.UtcNow;
        var prefix = kind.Contains("SUB", StringComparison.OrdinalIgnoreCase) || kind.Contains("MEMBER", StringComparison.OrdinalIgnoreCase) ? "⭐" : "💛";
        var value = string.IsNullOrWhiteSpace(amount) ? kind : amount;
        var chat = new ChatMessage
        {
            Platform = platform,
            ExternalId = "event-chat:" + id,
            User = user,
            Role = "Donor",
            Time = DateTime.Now,
            Text = $"{prefix} {value}" + (string.IsNullOrWhiteSpace(message) ? string.Empty : " • " + message),
            Foreground = "#FFD329"
        };
        Chat.Add(chat);
        while (Chat.Count > 220) Chat.RemoveAt(0);
        ChatAdded?.Invoke(this, chat);
    }

    private void AddEvent(LiteEvent e)
    {
        Events.Insert(0, e);
        while (Events.Count > 100) Events.RemoveAt(Events.Count - 1);
        EventAdded?.Invoke(this, e);
    }

    private bool RecentlySeen(string key, TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        foreach (var old in _recent.Where(x => now - x.Value > TimeSpan.FromMinutes(2)).Select(x => x.Key).ToList()) _recent.Remove(old);
        if (_recent.TryGetValue(key, out var at) && now - at < ttl) return true;
        _recent[key] = now;
        return false;
    }

    public async Task SendChatAsync(string text, string target)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var errors = new List<string>();
        if (target is "TWITCH" or "BOTH")
        {
            try { await Twitch.SendMessageAsync(text).ConfigureAwait(false); } catch (Exception ex) { errors.Add("Twitch: " + ex.Message); }
        }
        if (target is "YOUTUBE" or "BOTH")
        {
            try { await YouTube.SendMessageAsync(text).ConfigureAwait(false); } catch (Exception ex) { errors.Add("YouTube: " + ex.Message); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    public Task ModerateAsync(ChatMessage m, bool ban, int timeoutSeconds = 600)
    {
        if (m.Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase))
            return ban ? Twitch.BanUserAsync(m.AuthorId, "TiHiY MINI") : Twitch.TimeoutUserAsync(m.AuthorId, timeoutSeconds, "TiHiY MINI");
        if (m.Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
            return ban ? YouTube.BanUserAsync(m.AuthorId) : YouTube.TimeoutUserAsync(m.AuthorId, timeoutSeconds);
        return Task.CompletedTask;
    }

    public Task DeleteMessageAsync(ChatMessage m)
    {
        if (m.Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase)) return Twitch.DeleteMessageAsync(m.ExternalId);
        if (m.Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase)) return YouTube.DeleteMessageAsync(m.ExternalId);
        return Task.CompletedTask;
    }

    public string CurrentSong()
    {
        if (!Preferences.Value.HudShowAimp) return string.Empty;
        try { return _aimp.Value.CurrentSongText(); } catch { return string.Empty; }
    }

    private void UpdateStats()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            Stats.TwitchViewers = Settings.Value.TwitchViewers;
            Stats.YouTubeViewers = Settings.Value.YouTubeViewers;
            Stats.YouTubeLikes = Settings.Value.YouTubeLikes;
            RaiseStatus();
        }));
    }

    private void RaiseStatus() => Application.Current.Dispatcher.BeginInvoke(new Action(() => StatusChanged?.Invoke(this, EventArgs.Empty)));

    private async Task SafeAsync(Func<Task> work, string name)
    {
        try { await work().ConfigureAwait(false); }
        catch (Exception ex) { Logger.Error(name, ex); RaiseStatus(); }
    }

    private static string NormalizePlatform(string source) => source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase) ? "TWITCH"
        : source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase) ? "YOUTUBE"
        : source.Contains("DONATELLO", StringComparison.OrdinalIgnoreCase) ? "DONATELLO" : source.ToUpperInvariant();

    private static string NormalizeMoneyKind(DonationEvent d) => d.Kind.Equals("SUBSCRIPTION", StringComparison.OrdinalIgnoreCase)
        ? (d.Source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase) ? "MEMBER" : "SUB")
        : d.Source.Contains("BITS", StringComparison.OrdinalIgnoreCase) ? "BITS"
        : d.Source.Contains("SUPER", StringComparison.OrdinalIgnoreCase) ? "SUPER CHAT" : d.Kind.Equals("GIFT", StringComparison.OrdinalIgnoreCase) ? "GIFT" : "DONATION";

    private static string NormalizeStreamlabsType(string type) => type.ToLowerInvariant() switch
    {
        "follow" => "FOLLOW",
        "subscription" => "SUB",
        "sponsor" => "MEMBER",
        "bits" => "BITS",
        "raid" or "raids" => "RAID",
        "host" => "HOST",
        "superchat" => "SUPER CHAT",
        "donation" => "DONATION",
        _ => type.ToUpperInvariant()
    };

    private static string Fingerprint(string p, string t, string u, decimal amount, string currency) => $"{p}|{t}|{u}|{amount:0.##}|{currency}".ToLowerInvariant();
    private static string BuildStreamlabsText(string type, string text, decimal amount, string currency)
    {
        var value = amount > 0 ? FormatAmount(amount, currency) : string.Empty;
        return string.Join(" • ", new[] { value, text }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
    private static string FormatAmount(decimal amount, string currency) => amount <= 0 ? string.Empty : $"{amount:0.##} {currency}".Trim();
    private static string? First(JsonElement e, params string[] names)
    {
        foreach (var n in names) if (e.TryGetProperty(n, out var v)) { var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString(); if (!string.IsNullOrWhiteSpace(s)) return s; }
        return null;
    }
    private static decimal ReadDecimal(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        Preferences.Save();
        SettingsService.Save(Settings.Value);
        try { await Streamlabs.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await Donatello.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await NotifyBot.DisposeAsync().ConfigureAwait(false); } catch { }
        try { Discord.Dispose(); } catch { }
        try { await Twitch.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await YouTube.DisposeAsync().ConfigureAwait(false); } catch { }
        if (_aimp.IsValueCreated) try { await _aimp.Value.DisposeAsync().ConfigureAwait(false); } catch { }
    }
}
