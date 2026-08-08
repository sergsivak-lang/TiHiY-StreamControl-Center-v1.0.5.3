using TiHiY.StreamControlCenter.Models;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public sealed class LiteChatBotService : IDisposable
{
    private readonly AppSettingsAccessor _settings;
    private readonly SettingsService _settingsService;
    private readonly AppLogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTime> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastMessageByAuthor = new(StringComparer.OrdinalIgnoreCase);
    private int _messagesSinceNotice;

    public ObservableCollection<ScheduledNotice> Notices { get; } = new();
    public ObservableCollection<BotCommand> Commands { get; } = new();
    public Func<string, string, Task>? Sender { get; set; }
    public Func<string>? SongProvider { get; set; }
    public bool IsRunning { get; private set; }
    public string Status => IsRunning ? "ПРАЦЮЄ" : "ЗУПИНЕНО";
    public event EventHandler? StatusChanged;

    public LiteChatBotService(AppSettingsAccessor settings, SettingsService settingsService, AppLogger logger)
    {
        _settings = settings;
        _settingsService = settingsService;
        _logger = logger;
        foreach (var n in settings.Value.ScheduledNotices ?? new List<ScheduledNotice>())
        {
            if (n.NextRun <= DateTime.Now) n.NextRun = DateTime.Now.AddMinutes(Math.Max(1, n.IntervalMinutes));
            Notices.Add(n);
        }
        foreach (var c in settings.Value.BotCommands ?? new List<BotCommand>()) Commands.Add(c);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += Timer_Tick;
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _timer.Start();
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _logger.Info("Lite Chat Bot: запущено");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _timer.Stop();
        IsRunning = false;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _logger.Info("Lite Chat Bot: зупинено");
    }

    public void ProcessIncoming(ChatMessage message)
    {
        if (!IsRunning) return;
        _messagesSinceNotice++;
        if (!_settings.Value.ChatBotEnabled || string.Equals(message.Role, "Bot", StringComparison.OrdinalIgnoreCase)) return;
        if (ShouldSuppress(message)) return;

        var text = (message.Text ?? string.Empty).Trim();
        var command = Commands.FirstOrDefault(c => c.Enabled && string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
        if (command is null) return;
        if (!(command.Target.Contains(message.Platform, StringComparison.OrdinalIgnoreCase) || command.Target.Contains("Twitch + YouTube", StringComparison.OrdinalIgnoreCase))) return;

        var now = DateTime.Now;
        if (_cooldowns.TryGetValue(command.Name, out var last) && (now - last).TotalSeconds < Math.Max(0, command.CooldownSeconds)) return;
        _cooldowns[command.Name] = now;
        var reply = (command.Reply ?? string.Empty).Replace("{song}", SongProvider?.Invoke() ?? "нічого", StringComparison.OrdinalIgnoreCase);
        _ = SendSafeAsync(reply, string.IsNullOrWhiteSpace(command.Target) ? _settings.Value.ChatBotDefaultTarget : command.Target);
    }

    public async Task SendNowAsync(string text, string target)
    {
        if (Sender is null) throw new InvalidOperationException("Канали чату ще не налаштовані.");
        if (string.IsNullOrWhiteSpace(text)) return;
        await Sender(text.Trim(), target).ConfigureAwait(false);
    }

    public void SaveAll()
    {
        _settings.Value.ScheduledNotices = Notices.ToList();
        _settings.Value.BotCommands = Commands.ToList();
        _settingsService.Save(_settings.Value);
    }

    private bool ShouldSuppress(ChatMessage message)
    {
        var s = _settings.Value;
        if (!s.ChatBotSpamProtectionEnabled) return false;
        var text = (message.Text ?? string.Empty).Trim();
        if (text.Length == 0) return true;
        if (s.ChatBotBlockLinks && (text.Contains("http://", StringComparison.OrdinalIgnoreCase) || text.Contains("https://", StringComparison.OrdinalIgnoreCase) || text.Contains("www.", StringComparison.OrdinalIgnoreCase))) return true;
        if (s.ChatBotBlockCaps)
        {
            var letters = text.Where(char.IsLetter).ToArray();
            if (letters.Length >= 8 && letters.Count(char.IsUpper) >= letters.Length * .75) return true;
        }
        if (s.ChatBotBlockRepeats)
        {
            var key = string.IsNullOrWhiteSpace(message.AuthorId) ? $"{message.Platform}:{message.User}" : message.AuthorId;
            if (_lastMessageByAuthor.TryGetValue(key, out var previous) && string.Equals(previous, text, StringComparison.OrdinalIgnoreCase)) return true;
            _lastMessageByAuthor[key] = text;
        }
        var blocked = (s.ChatBotBlockedWords ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return blocked.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (!IsRunning || !_settings.Value.AutoNoticesEnabled) return;
        var now = DateTime.Now;
        foreach (var n in Notices.Where(x => x.Enabled && x.NextRun <= now).ToList())
        {
            if (_messagesSinceNotice < n.MinimumChatMessages)
            {
                n.NextRun = now.AddMinutes(1);
                continue;
            }
            try
            {
                await SendNowAsync(n.Text, string.IsNullOrWhiteSpace(n.Target) ? _settings.Value.ChatBotDefaultTarget : n.Target);
                n.LastSent = now;
                n.NextRun = now.AddMinutes(Math.Max(1, n.IntervalMinutes));
                _messagesSinceNotice = 0;
                _logger.Info($"Lite Chat Bot: автосповіщення {n.Name}");
            }
            catch (Exception ex) { _logger.Error("Lite Chat Bot: автосповіщення", ex); }
        }
    }

    private async Task SendSafeAsync(string text, string target)
    {
        try
        {
            var delay = Math.Clamp(_settings.Value.ChatBotResponseDelayMilliseconds, 0, 10000);
            if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
            await SendNowAsync(text, target).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.Error("Lite Chat Bot: відповідь", ex); }
    }

    public void Dispose() => Stop();
}
