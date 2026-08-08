using System.Text.Json;

namespace TiHiY.StreamControlCenter;

public sealed class LitePreferences
{
    public string StreamlabsClientId { get; set; } = string.Empty;
    public bool StreamlabsAutoConnect { get; set; } = true;
    public bool StreamlabsControlAlerts { get; set; } = true;
    public bool HudAutoStart { get; set; } = true;
    public bool HudClickThrough { get; set; } = true;
    public bool HudExcludeFromCapture { get; set; } = true;
    public double HudOpacity { get; set; } = 0.92;
    public double HudBackgroundOpacity { get; set; } = 0.38;
    public double HudFontSize { get; set; } = 17;
    public int HudMaxMessages { get; set; } = 8;
    public double HudLeft { get; set; } = 24;
    public double HudTop { get; set; } = 180;
    public double HudWidth { get; set; } = 520;
    public double HudHeight { get; set; } = 430;
    public bool HudShowEvents { get; set; } = true;
    public bool HudShowStats { get; set; } = true;
    public bool HudShowAimp { get; set; }
    public bool MinimizeToTray { get; set; } = true;
}

public sealed class LitePreferencesStore
{
    private readonly string _file;
    private readonly object _gate = new();
    public LitePreferences Value { get; private set; }

    public LitePreferencesStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiHiY", "StreamControlMini");
        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "lite.json");
        Value = Load();
    }

    private LitePreferences Load()
    {
        try
        {
            return File.Exists(_file)
                ? JsonSerializer.Deserialize<LitePreferences>(File.ReadAllText(_file)) ?? new LitePreferences()
                : new LitePreferences();
        }
        catch { return new LitePreferences(); }
    }

    public void Save()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_file + ".tmp", json);
            File.Move(_file + ".tmp", _file, true);
        }
    }
}
