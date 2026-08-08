using System.Text.Json;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

public sealed class SettingsService
{
    private readonly string _folder;
    private readonly string _file;
    private readonly string _legacyFile;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _gate = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _folder = Path.Combine(appData, "TiHiY", "StreamControlMini");
        _file = Path.Combine(_folder, "settings.json");
        _legacyFile = Path.Combine(appData, "TiHiY", "StreamControlCenter", "settings.json");
    }

    public string Folder => _folder;

    public AppSettings Load()
    {
        try
        {
            var source = File.Exists(_file) ? _file : File.Exists(_legacyFile) ? _legacyFile : string.Empty;
            var settings = string.IsNullOrWhiteSpace(source)
                ? CreateDefaults()
                : JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(source), _options) ?? CreateDefaults();

            Normalize(settings);
            settings.UiTheme = "Україна";

            // MINI не використовує OBS Audio та PC/AIDA64 dashboard.
            // Віджети, Twitch, YouTube, Donatello та Discord від цього не залежать.
            settings.AutoConnectObs = false;
            settings.Aida64MonitoringEnabled = false;

            if (!File.Exists(_file))
                Save(settings);

            return settings;
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            settings.UiTheme = "Україна";
            settings.AutoConnectObs = false;
            settings.Aida64MonitoringEnabled = false;
            Directory.CreateDirectory(_folder);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, _options));
            File.Move(temp, _file, true);
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.SelectedAudioInputs ??= new List<string>();
        settings.PinnedAudioInputs ??= new List<string>();
        settings.ScheduledNotices ??= new List<ScheduledNotice>();
        settings.BotCommands ??= new List<BotCommand>();
        settings.MusicPlaylistPaths ??= new List<string>();
        settings.WindowPlacements ??= new Dictionary<string, WindowPlacement>(StringComparer.OrdinalIgnoreCase);
        settings.DonatelloRecentEventIds ??= new List<string>();
        settings.DonatelloSubscriberPayments ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static AppSettings CreateDefaults()
    {
        var result = new AppSettings
        {
            UiTheme = "Україна",
            OverlayTheme = "TiHiY-DED Ukraine",
            AutoConnectObs = false,
            Aida64MonitoringEnabled = false
        };
        result.BotCommands.Add(new BotCommand { Name = "!song", Reply = "Зараз грає: {song}", Target = "Twitch + YouTube", CooldownSeconds = 10 });
        result.ScheduledNotices.Add(new ScheduledNotice
        {
            Name = "Підтримка каналу",
            Text = "Підтримати канал: donatello.to/TiHiY-DED",
            Target = "Twitch + YouTube",
            IntervalMinutes = 25,
            MinimumChatMessages = 10,
            Enabled = false,
            NextRun = DateTime.Now.AddMinutes(25)
        });
        return result;
    }
}
