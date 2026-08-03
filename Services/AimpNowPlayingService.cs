using System.Diagnostics;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Lightweight AIMP integration for MINI.
/// No polling timer and no audio engine are created: the current track is read only
/// when the overlay/API asks for /api/now-playing.
/// </summary>
public sealed class AimpNowPlayingService
{
    private static readonly string[] ProcessPrefixes = { "AIMP" };
    private static readonly string[] Separators = { " — ", " – ", " - " };
    private readonly object _gate = new();
    private DateTime _lastReadUtc = DateTime.MinValue;
    private AimpTrackSnapshot _cached = new(false, string.Empty, string.Empty, 0, 0, "AIMP");

    public AimpTrackSnapshot Read()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _lastReadUtc < TimeSpan.FromMilliseconds(800))
                return _cached;

            _lastReadUtc = DateTime.UtcNow;
            _cached = ReadCore();
            return _cached;
        }
    }

    public string CurrentSongText()
    {
        var track = Read();
        if (!track.Active) return "нічого";
        if (!string.IsNullOrWhiteSpace(track.Artist) && !string.IsNullOrWhiteSpace(track.Title))
            return $"{track.Artist} — {track.Title}";
        return string.IsNullOrWhiteSpace(track.Title) ? "AIMP" : track.Title;
    }

    private static AimpTrackSnapshot ReadCore()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return new AimpTrackSnapshot(false, string.Empty, string.Empty, 0, 0, "AIMP"); }

        try
        {
            var aimp = processes
                .Where(p => SafeProcessNameStartsWithAimp(p))
                .OrderByDescending(SafeHasMainWindow)
                .FirstOrDefault();

            if (aimp is null)
                return new AimpTrackSnapshot(false, string.Empty, string.Empty, 0, 0, "AIMP");

            var caption = SafeCaption(aimp);
            if (string.IsNullOrWhiteSpace(caption))
                return new AimpTrackSnapshot(true, string.Empty, string.Empty, 0, 0, "AIMP");

            caption = CleanCaption(caption);
            var (artist, title) = SplitArtistAndTitle(caption);
            return new AimpTrackSnapshot(
                true,
                string.IsNullOrWhiteSpace(title) ? caption : title,
                artist,
                0,
                0,
                "AIMP");
        }
        catch
        {
            return new AimpTrackSnapshot(false, string.Empty, string.Empty, 0, 0, "AIMP");
        }
        finally
        {
            foreach (var process in processes)
            {
                try { process.Dispose(); } catch { }
            }
        }
    }

    private static bool SafeProcessNameStartsWithAimp(Process process)
    {
        try
        {
            var name = process.ProcessName;
            return ProcessPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static bool SafeHasMainWindow(Process process)
    {
        try { return process.MainWindowHandle != IntPtr.Zero; }
        catch { return false; }
    }

    private static string SafeCaption(Process process)
    {
        try { return process.MainWindowTitle?.Trim() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string CleanCaption(string value)
    {
        var text = value.Trim();
        foreach (var suffix in new[] { " - AIMP", " — AIMP", " – AIMP", " [AIMP]", " | AIMP" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^suffix.Length].Trim();
                break;
            }
        }
        return text;
    }

    private static (string Artist, string Title) SplitArtistAndTitle(string caption)
    {
        foreach (var separator in Separators)
        {
            var index = caption.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index + separator.Length >= caption.Length) continue;
            var left = caption[..index].Trim();
            var right = caption[(index + separator.Length)..].Trim();
            if (left.Length > 0 && right.Length > 0)
                return (left, right);
        }
        return (string.Empty, caption.Trim());
    }
}

public sealed record AimpTrackSnapshot(
    bool Active,
    string Title,
    string Artist,
    double PositionSeconds,
    double DurationSeconds,
    string Source);
