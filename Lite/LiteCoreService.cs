using System.Globalization;
using System.Text.Json;
using TiHiY.StreamControlCenter.Models;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public sealed class LiteCoreService : IAsyncDisposable
{
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _recentDonorChat = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Count, DateTime At)> _recentOutgoing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly object _chatGate = new();
    private readonly Lazy<AimpNowPlayingService> _aimp = new(() => new AimpNowPlayingService());
    private AimpStreamOverlayServer? _aimpStreamOverlay;
    private int _disposeState;

    private static readonly string[] TwitchFallbackPalette =
    {
        "#FF0000", "#0000FF", "#008000", "#B22222", "#FF7F50", "#9ACD32",
        "#FF4500", "#2E8B57", "#DAA520", "#D2691E", "#5F9EA0", "#1E90FF",
        "#FF69B4", "#8A2BE2", "#00FF7F"
    };

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
    public LiteChatBotService ChatBot { get; }
    public LiteStats Stats { get; } = new();
    public ObservableCollection<ChatMessage> Chat { get; } = new();
    public ObservableCollection<LiteEvent> Events { get; } = new();
    public ObservableCollection<DonationEvent> Donations { get; } = new();
    public AimpNowPlayingService Aimp => _aimp.Value;
    public AimpStreamOverlayServer? AimpStreamOverlay => _aimpStreamOverlay;

    public event EventHandler? StatusChanged;
    public event EventHandler<ChatMessage>? ChatAdded;
    public event EventHandler<LiteEvent>? EventAdded;

    public LiteCoreService()
    {
        Settings.Value = SettingsService.Load();
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
        ChatBot = new LiteChatBotService(Settings, SettingsService, Logger)
        {
            Sender = SendChatAsync,
            SongProvider = CurrentSong
        };

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
        ChatBot.StatusChanged += (_, _) => RaiseStatus();
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
        if (Settings.Value.ChatBotAutoStart)
            ChatBot.Start();

        if (Preferences.Value.HudShowAimp || Preferences.Value.AimpStreamOverlayEnabled)
            _ = SafeAsync(() => _aimp.Value.InitializeAsync(), "AIMP metadata");
        if (Preferences.Value.AimpStreamOverlayEnabled)
            _ = SafeAsync(EnsureAimpStreamOverlayAsync, "AIMP stream overlay");

        UpdateStats();
    }

    public async Task EnsureAimpStreamOverlayAsync()
    {
        if (!Preferences.Value.AimpStreamOverlayEnabled)
        {
            if (_aimpStreamOverlay is not null)
            {
                await _aimpStreamOverlay.DisposeAsync().ConfigureAwait(false);
                _aimpStreamOverlay = null;
            }
            return;
        }

        await _aimp.Value.InitializeAsync().ConfigureAwait(false);
        if (_aimpStreamOverlay is null) _aimpStreamOverlay = new AimpStreamOverlayServer(_aimp.Value);
        if (!_aimpStreamOverlay.IsRunning)
            await _aimpStreamOverlay.StartAsync(Preferences.Value.AimpStreamOverlayPort).ConfigureAwait(false);
    }

    private async Task AddChatAsync(ChatMessage message)
    {
        if (ConsumeOutgoingEcho(message)) return;
        lock (_chatGate)
        {
            if (!string.IsNullOrWhiteSpace(message.ExternalId) && !_messageIds.Add(message.ExternalId)) return;
        }

        ApplyPlatformNicknameColor(message);
        try { await LiteEmojiResolver.EnrichAsync(message, YouTube.ActiveBroadcastId).ConfigureAwait(false); }
        catch (Exception ex) { Logger.Error("Emoji resolver", ex); }

        Application.Current.Dispatcher.BeginInvoke(new Action(() => AppendChat(message, true)));
    }

    private void AppendChat(ChatMessage message, bool processBot)
    {
        Chat.Add(message);
        while (Chat.Count > 220) Chat.RemoveAt(0);
        if (message.Role.Equals("Donor", StringComparison.OrdinalIgnoreCase) || message.Role.Equals("Subscriber", StringComparison.OrdinalIgnoreCase))
            _recentDonorChat[$"{NormalizePlatform(message.Platform)}:{message.User}".ToLowerInvariant()] = DateTime.UtcNow;
        ChatAdded?.Invoke(this, message);
        if (processBot) ChatBot.ProcessIncoming(message);
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
                        Kind = "DONATION",
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

        var isStreamlabs = id.StartsWith("streamlabs:", StringComparison.OrdinalIgnoreCase);
        var text = isStreamlabs
            ? (string.IsNullOrWhiteSpace(message) ? kind : message)
            : BuildMoneyChatText(kind, amount, message);

        var chat = new ChatMessage
        {
            Platform = platform,
            ExternalId = "event-chat:" + id,
            User = user,
            Role = "Donor",
            Time = DateTime.Now,
            Text = text,
            Foreground = platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase) ? "#2BA640" : "#FFD329"
        };
        AppendChat(chat, false);
    }

    private static string BuildMoneyChatText(string kind, string amount, string message)
    {
        var prefix = kind.Contains("SUB", StringComparison.OrdinalIgnoreCase) || kind.Contains("MEMBER", StringComparison.OrdinalIgnoreCase) ? "⭐" : "💛";
        var value = string.IsNullOrWhiteSpace(amount) ? kind : amount;
        return $"{prefix} {value}" + (string.IsNullOrWhiteSpace(message) ? string.Empty : " • " + message);
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
        text = text.Trim();
        var normalized = (target ?? string.Empty).Trim().ToUpperInvariant();
        var both = normalized is "BOTH" or "TWITCH + YOUTUBE" or "TWITCH+YOUTUBE";
        var twitch = both || normalized == "TWITCH";
        var youtube = both || normalized == "YOUTUBE";
        var errors = new List<string>();

        if (twitch)
        {
            RegisterOutgoing("TWITCH", text);
            try
            {
                await Twitch.SendMessageAsync(text).ConfigureAwait(false);
                await AddOutgoingEchoAsync("TWITCH", text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RemoveOutgoing("TWITCH", text);
                errors.Add("Twitch: " + ex.Message);
            }
        }
        if (youtube)
        {
            RegisterOutgoing("YOUTUBE", text);
            try
            {
                await YouTube.SendMessageAsync(text).ConfigureAwait(false);
                await AddOutgoingEchoAsync("YOUTUBE", text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RemoveOutgoing("YOUTUBE", text);
                errors.Add("YouTube: " + ex.Message);
            }
        }
        if (!twitch && !youtube) throw new InvalidOperationException("Невідомий канал чату: " + target);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    private async Task AddOutgoingEchoAsync(string platform, string text)
    {
        var message = new ChatMessage
        {
            Platform = platform,
            ExternalId = $"local:{platform.ToLowerInvariant()}:{Guid.NewGuid():N}",
            User = OwnDisplayName(platform),
            Text = text,
            Role = "Owner",
            Time = DateTime.Now,
            Foreground = PlatformNicknameColor(platform, "Owner", OwnDisplayName(platform))
        };
        try { await LiteEmojiResolver.EnrichAsync(message, YouTube.ActiveBroadcastId).ConfigureAwait(false); }
        catch (Exception ex) { Logger.Error("Emoji resolver outgoing", ex); }
        Application.Current.Dispatcher.BeginInvoke(new Action(() => AppendChat(message, false)));
    }

    private void RegisterOutgoing(string platform, string text)
    {
        var key = OutgoingKey(platform, text);
        lock (_chatGate)
        {
            CleanupOutgoingLocked();
            if (_recentOutgoing.TryGetValue(key, out var existing))
                _recentOutgoing[key] = (existing.Count + 1, DateTime.UtcNow);
            else
                _recentOutgoing[key] = (1, DateTime.UtcNow);
        }
    }

    private void RemoveOutgoing(string platform, string text)
    {
        var key = OutgoingKey(platform, text);
        lock (_chatGate)
        {
            if (!_recentOutgoing.TryGetValue(key, out var existing)) return;
            if (existing.Count <= 1) _recentOutgoing.Remove(key);
            else _recentOutgoing[key] = (existing.Count - 1, existing.At);
        }
    }

    private bool ConsumeOutgoingEcho(ChatMessage message)
    {
        if (!IsOwnChatUser(message)) return false;
        var key = OutgoingKey(message.Platform, message.Text);
        lock (_chatGate)
        {
            CleanupOutgoingLocked();
            if (!_recentOutgoing.TryGetValue(key, out var existing)) return false;
            if (existing.Count <= 1) _recentOutgoing.Remove(key);
            else _recentOutgoing[key] = (existing.Count - 1, existing.At);
            return true;
        }
    }

    private void CleanupOutgoingLocked()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _recentOutgoing.Where(x => now - x.Value.At > TimeSpan.FromSeconds(60)).Select(x => x.Key).ToList())
            _recentOutgoing.Remove(key);
    }

    private bool IsOwnChatUser(ChatMessage message)
    {
        static string Clean(string value) => (value ?? string.Empty).Trim().TrimStart('@');
        if (message.Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase))
        {
            var expected = string.IsNullOrWhiteSpace(Settings.Value.TwitchUserLogin) ? Settings.Value.TwitchChannelName : Settings.Value.TwitchUserLogin;
            return Clean(message.User).Equals(Clean(expected), StringComparison.OrdinalIgnoreCase);
        }
        if (message.Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
            return Clean(message.User).Equals(Clean(Settings.Value.YouTubeChannelName), StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private string OwnDisplayName(string platform)
    {
        if (platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(Settings.Value.TwitchUserLogin) ? Settings.Value.TwitchChannelName : Settings.Value.TwitchUserLogin;
        if (platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
        {
            var value = Settings.Value.YouTubeChannelName.Trim();
            return value.StartsWith('@') ? value : "@" + value;
        }
        return "TiHiY-DED";
    }

    private static string OutgoingKey(string platform, string text) => $"{platform.Trim().ToUpperInvariant()}|{text.Trim()}";

    private void ApplyPlatformNicknameColor(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Foreground) &&
            !message.Foreground.Equals("#EDF7FF", StringComparison.OrdinalIgnoreCase) &&
            !message.Foreground.Equals("#F2FAFF", StringComparison.OrdinalIgnoreCase)) return;
        message.Foreground = PlatformNicknameColor(message.Platform, message.Role, message.User);
    }

    private static string PlatformNicknameColor(string platform, string role, string user)
    {
        if (platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
        {
            if (role.Equals("Owner", StringComparison.OrdinalIgnoreCase)) return "#FFD600";
            if (role.Equals("Moderator", StringComparison.OrdinalIgnoreCase)) return "#5E84F1";
            if (role.Equals("Subscriber", StringComparison.OrdinalIgnoreCase)) return "#2BA640";
            if (role.Equals("Donor", StringComparison.OrdinalIgnoreCase)) return "#00C8FF";
            return "#B8C7D9";
        }
        if (platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase))
        {
            if (role.Equals("Owner", StringComparison.OrdinalIgnoreCase)) return "#FFD329";
            if (role.Equals("Moderator", StringComparison.OrdinalIgnoreCase)) return "#00AD03";
            if (role.Equals("VIP", StringComparison.OrdinalIgnoreCase)) return "#E91916";
            if (role.Equals("Subscriber", StringComparison.OrdinalIgnoreCase)) return "#A970FF";
            return TwitchFallbackPalette[StableHash(user) % TwitchFallbackPalette.Length];
        }
        if (platform.Equals("DONATELLO", StringComparison.OrdinalIgnoreCase)) return "#FFD329";
        return "#55C8FF";
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value ?? string.Empty) hash = hash * 31 + char.ToUpperInvariant(c);
            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }
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
        if (!Preferences.Value.HudShowAimp && !Preferences.Value.AimpStreamOverlayEnabled) return string.Empty;
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
        : d.Source.Contains("SUPER", StringComparison.OrdinalIgnoreCase) ? "SUPER CHAT"
        : d.Kind.Equals("GIFT", StringComparison.OrdinalIgnoreCase) ? "GIFT" : "DONATION";

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
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v))
            {
                var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
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
        try { ChatBot.Dispose(); } catch { }
        if (_aimpStreamOverlay is not null) try { await _aimpStreamOverlay.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await Streamlabs.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await Donatello.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await NotifyBot.DisposeAsync().ConfigureAwait(false); } catch { }
        try { Discord.Dispose(); } catch { }
        try { await Twitch.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await YouTube.DisposeAsync().ConfigureAwait(false); } catch { }
        if (_aimp.IsValueCreated) try { await _aimp.Value.DisposeAsync().ConfigureAwait(false); } catch { }
    }
}
