using System.Text.Json;

namespace TiHiY.StreamControlCenter.Services;

public sealed class AimpWidgetPreferences
{
    public int BackgroundOpacityPercent { get; set; } = 72;
    public int HeightPixels { get; set; } = 92;

    public static AimpWidgetPreferences Load(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "aimp-widget.json");
            if (!File.Exists(file)) return new AimpWidgetPreferences();
            var result = JsonSerializer.Deserialize<AimpWidgetPreferences>(File.ReadAllText(file)) ?? new AimpWidgetPreferences();
            result.Normalize();
            return result;
        }
        catch
        {
            return new AimpWidgetPreferences();
        }
    }

    public void Save(string folder)
    {
        Normalize();
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "aimp-widget.json");
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, true);
    }

    public string ApplyToUrl(string baseUrl)
    {
        var clean = baseUrl.Split('?', 2)[0];
        return $"{clean}?opacity={BackgroundOpacityPercent}&height={HeightPixels}";
    }

    private void Normalize()
    {
        BackgroundOpacityPercent = Math.Clamp(BackgroundOpacityPercent, 0, 100);
        HeightPixels = Math.Clamp(HeightPixels, 72, 140);
    }
}
