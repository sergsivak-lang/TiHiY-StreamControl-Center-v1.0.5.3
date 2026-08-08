using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Event-driven AIMP metadata integration via Windows Global Media Sessions.
/// Exact position/duration are read on demand from the official AIMP Remote Access API.
/// There is no background polling timer: a cached Remote API read happens only when the
/// overlay/API requests the current state, at most twice per second.
/// </summary>
public sealed class AimpNowPlayingService : IAsyncDisposable
{
    private const string AimpRemoteWindowClass = "AIMP2_RemoteInfo";
    private const uint WmUser = 0x0400;
    private const uint WmAimpProperty = WmUser + 0x77;
    private const uint AimpPropertyPlayerPosition = 0x20;
    private const uint AimpPropertyPlayerDuration = 0x30;
    private const uint AimpPropertyPlayerState = 0x40;
    private const uint SmtoAbortIfHung = 0x0002;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private AimpTrackSnapshot _snapshot = AimpTrackSnapshot.Empty;
    private AimpCoverPayload _cover = AimpCoverPayload.Empty;
    private DateTimeOffset _timelineCapturedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastFallbackScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRemoteTimelineRead = DateTimeOffset.MinValue;
    private bool _disposed;

    public event EventHandler? Changed;

    public async Task InitializeAsync()
    {
        if (_disposed || _manager is not null) return;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += Manager_SessionsChanged;
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            await SelectAimpSessionAsync().ConfigureAwait(false);
        }
        catch
        {
            // Windows media-session integration may be unavailable/disabled.
            // Window-title fallback and AIMP Remote Access timeline remain available.
        }
    }

    public AimpTrackSnapshot Read()
    {
        bool needsFallback;
        lock (_gate) needsFallback = !_snapshot.Active || string.IsNullOrWhiteSpace(_snapshot.Title);
        if (needsFallback) ReadFallbackTitleIfNeeded();

        RefreshRemoteTimelineIfDue();

        lock (_gate)
        {
            var current = _snapshot;
            if (current.Active && current.IsPlaying && current.DurationSeconds > 0)
            {
                var elapsed = Math.Max(0, (DateTimeOffset.UtcNow - _timelineCapturedAt).TotalSeconds);
                var position = Math.Clamp(current.PositionSeconds + elapsed, 0, current.DurationSeconds);
                return current with { PositionSeconds = position };
            }
            return current;
        }
    }

    public AimpCoverPayload ReadCover()
    {
        lock (_gate) return _cover;
    }

    public string CurrentSongText()
    {
        var track = Read();
        if (!track.Active) return "нічого";
        if (!string.IsNullOrWhiteSpace(track.Artist) && !string.IsNullOrWhiteSpace(track.Title))
            return $"{track.Artist} — {track.Title}";
        return string.IsNullOrWhiteSpace(track.Title) ? "AIMP" : track.Title;
    }

    public async Task<bool> ControlAsync(string action)
    {
        GlobalSystemMediaTransportControlsSession? session;
        AimpTrackSnapshot snapshot;
        lock (_gate)
        {
            session = _session;
            snapshot = _snapshot;
        }
        if (session is null) return false;

        try
        {
            return action.Trim().ToLowerInvariant() switch
            {
                "play" => await session.TryPlayAsync(),
                "pause" => await session.TryPauseAsync(),
                "toggle" => snapshot.IsPlaying ? await session.TryPauseAsync() : await session.TryPlayAsync(),
                "next" => await session.TrySkipNextAsync(),
                "previous" or "prev" => await session.TrySkipPreviousAsync(),
                _ => false
            };
        }
        catch { return false; }
    }

    private void RefreshRemoteTimelineIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (now - _lastRemoteTimelineRead < TimeSpan.FromMilliseconds(500)) return;
            _lastRemoteTimelineRead = now;
        }

        var window = FindWindow(AimpRemoteWindowClass, null);
        if (window == IntPtr.Zero) return;

        if (!TryReadAimpProperty(window, AimpPropertyPlayerState, out var rawState)) return;
        _ = TryReadAimpProperty(window, AimpPropertyPlayerPosition, out var rawPosition);
        _ = TryReadAimpProperty(window, AimpPropertyPlayerDuration, out var rawDuration);

        var state = (int)Math.Clamp(rawState, 0, 2);
        var position = Math.Max(0, rawPosition) / 1000d;
        var duration = Math.Max(0, rawDuration) / 1000d;

        lock (_gate)
        {
            var current = _snapshot;
            var mode = current.IntegrationMode switch
            {
                "WindowsMediaSession" => "WindowsMediaSession+AIMPRemote",
                "WindowTitle" => "WindowTitle+AIMPRemote",
                "Unavailable" => "AIMPRemote",
                _ when current.IntegrationMode.Contains("AIMPRemote", StringComparison.OrdinalIgnoreCase) => current.IntegrationMode,
                _ => current.IntegrationMode + "+AIMPRemote"
            };

            _timelineCapturedAt = now;
            _snapshot = current with
            {
                Active = state != 0 || current.Active,
                PositionSeconds = position,
                DurationSeconds = duration,
                IsPlaying = state == 2,
                IntegrationMode = mode
            };
        }
    }

    private static bool TryReadAimpProperty(IntPtr window, uint property, out long value)
    {
        value = 0;
        try
        {
            var ok = SendMessageTimeout(
                window,
                WmAimpProperty,
                new UIntPtr(property),
                IntPtr.Zero,
                SmtoAbortIfHung,
                120,
                out var result);
            if (ok == IntPtr.Zero) return false;
            value = unchecked((long)result.ToUInt64());
            return true;
        }
        catch { return false; }
    }

    private async void Manager_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        try { await SelectAimpSessionAsync().ConfigureAwait(false); } catch { }
    }

    private async void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try { await SelectAimpSessionAsync().ConfigureAwait(false); } catch { }
    }

    private async Task SelectAimpSessionAsync()
    {
        if (_disposed || _manager is null) return;
        var next = _manager.GetSessions().FirstOrDefault(IsAimpSession);

        GlobalSystemMediaTransportControlsSession? previous;
        lock (_gate) previous = _session;
        if (ReferenceEquals(previous, next))
        {
            if (next is not null) await RefreshAsync(includeMedia: true, includeCover: false).ConfigureAwait(false);
            return;
        }

        DetachSession(previous);
        lock (_gate) _session = next;

        if (next is null)
        {
            lock (_gate)
            {
                _snapshot = AimpTrackSnapshot.Empty;
                _cover = AimpCoverPayload.Empty;
                _timelineCapturedAt = DateTimeOffset.UtcNow;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        AttachSession(next);
        await RefreshAsync(includeMedia: true, includeCover: true).ConfigureAwait(false);
    }

    private static bool IsAimpSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var id = session.SourceAppUserModelId ?? string.Empty;
            return id.Contains("aimp", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
        session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
    }

    private void DetachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return;
        try { session.MediaPropertiesChanged -= Session_MediaPropertiesChanged; } catch { }
        try { session.PlaybackInfoChanged -= Session_PlaybackInfoChanged; } catch { }
        try { session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged; } catch { }
    }

    private async void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        try { await RefreshAsync(includeMedia: true, includeCover: true).ConfigureAwait(false); } catch { }
    }

    private async void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try { await RefreshAsync(includeMedia: false, includeCover: false).ConfigureAwait(false); } catch { }
    }

    private async void Session_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        try { await RefreshAsync(includeMedia: false, includeCover: false).ConfigureAwait(false); } catch { }
    }

    private async Task RefreshAsync(bool includeMedia, bool includeCover)
    {
        if (_disposed) return;
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            GlobalSystemMediaTransportControlsSession? session;
            AimpTrackSnapshot previous;
            lock (_gate)
            {
                session = _session;
                previous = _snapshot;
            }
            if (session is null) return;

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var status = playback?.PlaybackStatus ?? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
            var isPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var active = status is not GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;

            var title = previous.Title;
            var artist = previous.Artist;
            var album = previous.Album;
            var coverVersion = previous.CoverVersion;

            GlobalSystemMediaTransportControlsSessionMediaProperties? media = null;
            if (includeMedia || string.IsNullOrWhiteSpace(title))
            {
                try { media = await session.TryGetMediaPropertiesAsync(); } catch { }
                if (media is not null)
                {
                    title = media.Title?.Trim() ?? string.Empty;
                    artist = media.Artist?.Trim() ?? string.Empty;
                    album = media.AlbumTitle?.Trim() ?? string.Empty;
                }
            }

            if (includeCover && media?.Thumbnail is not null)
            {
                var loaded = await LoadCoverAsync(media.Thumbnail).ConfigureAwait(false);
                if (loaded.Data.Length > 0)
                {
                    lock (_gate)
                    {
                        var nextVersion = _cover.Version + 1;
                        _cover = loaded with { Version = nextVersion };
                        coverVersion = nextVersion;
                    }
                }
                else
                {
                    lock (_gate)
                    {
                        _cover = AimpCoverPayload.Empty;
                        coverVersion = 0;
                    }
                }
            }
            else if (includeCover && media is not null && media.Thumbnail is null)
            {
                lock (_gate)
                {
                    _cover = AimpCoverPayload.Empty;
                    coverVersion = 0;
                }
            }

            var position = Math.Max(0, timeline?.Position.TotalSeconds ?? 0);
            var duration = Math.Max(0, timeline?.EndTime.TotalSeconds ?? 0);
            if (duration <= 0 && timeline is not null)
                duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);

            lock (_gate)
            {
                _timelineCapturedAt = DateTimeOffset.UtcNow;
                _snapshot = new AimpTrackSnapshot(
                    active,
                    title,
                    artist,
                    album,
                    position,
                    duration,
                    isPlaying,
                    coverVersion,
                    "AIMP",
                    "WindowsMediaSession");
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static async Task<AimpCoverPayload> LoadCoverAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 12 * 1024 * 1024) return AimpCoverPayload.Empty;
            var size = checked((uint)stream.Size);
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var loaded = await reader.LoadAsync(size);
            if (loaded == 0) return AimpCoverPayload.Empty;
            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            var contentType = string.IsNullOrWhiteSpace(stream.ContentType) ? "image/jpeg" : stream.ContentType;
            return new AimpCoverPayload(bytes, contentType, 0);
        }
        catch { return AimpCoverPayload.Empty; }
    }

    private AimpTrackSnapshot ReadFallbackTitleIfNeeded()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _lastFallbackScan < TimeSpan.FromSeconds(2)) return _snapshot;
            _lastFallbackScan = DateTimeOffset.UtcNow;
        }

        var fallback = ReadProcessTitleFallback();
        lock (_gate)
        {
            if (_session is null) _snapshot = fallback;
            return _snapshot;
        }
    }

    private static AimpTrackSnapshot ReadProcessTitleFallback()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return AimpTrackSnapshot.Empty; }

        try
        {
            var aimp = processes.Where(SafeProcessNameStartsWithAimp).OrderByDescending(SafeHasMainWindow).FirstOrDefault();
            if (aimp is null) return AimpTrackSnapshot.Empty;

            var caption = CleanCaption(SafeCaption(aimp));
            if (string.IsNullOrWhiteSpace(caption))
                return AimpTrackSnapshot.Empty with { Active = true, IntegrationMode = "WindowTitle" };

            var (artist, title) = SplitArtistAndTitle(caption);
            return new AimpTrackSnapshot(
                true,
                string.IsNullOrWhiteSpace(title) ? caption : title,
                artist,
                string.Empty,
                0,
                0,
                true,
                0,
                "AIMP",
                "WindowTitle");
        }
        catch { return AimpTrackSnapshot.Empty; }
        finally
        {
            foreach (var process in processes)
                try { process.Dispose(); } catch { }
        }
    }

    private static bool SafeProcessNameStartsWithAimp(Process process)
    {
        try { return process.ProcessName.StartsWith("AIMP", StringComparison.OrdinalIgnoreCase); }
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
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return text[..^suffix.Length].Trim();
        return text;
    }

    private static (string Artist, string Title) SplitArtistAndTitle(string caption)
    {
        foreach (var separator in new[] { " — ", " – ", " - " })
        {
            var index = caption.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index + separator.Length >= caption.Length) continue;
            var left = caption[..index].Trim();
            var right = caption[(index + separator.Length)..].Trim();
            if (left.Length > 0 && right.Length > 0) return (left, right);
        }
        return (string.Empty, caption.Trim());
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        var manager = _manager;
        var session = _session;
        if (manager is not null)
        {
            try { manager.SessionsChanged -= Manager_SessionsChanged; } catch { }
            try { manager.CurrentSessionChanged -= Manager_CurrentSessionChanged; } catch { }
        }
        DetachSession(session);
        _session = null;
        _manager = null;
        _refreshGate.Dispose();
        return ValueTask.CompletedTask;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
}

public sealed record AimpTrackSnapshot(
    bool Active,
    string Title,
    string Artist,
    string Album,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    int CoverVersion,
    string Source,
    string IntegrationMode)
{
    public static AimpTrackSnapshot Empty { get; } = new(
        false, string.Empty, string.Empty, string.Empty, 0, 0, false, 0, "AIMP", "Unavailable");
}

public sealed record AimpCoverPayload(byte[] Data, string ContentType, int Version)
{
    public static AimpCoverPayload Empty { get; } = new(Array.Empty<byte>(), "image/jpeg", 0);
}
