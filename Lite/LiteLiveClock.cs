using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

public sealed class LiteLiveClock : IDisposable
{
    private readonly LiteCoreService _core;
    private DateTimeOffset? _twitchStartedAtUtc;
    private DateTimeOffset? _youtubeStartedAtUtc;
    private bool _twitchLive;
    private bool _youtubeLive;

    public LiteLiveClock(LiteCoreService core)
    {
        _core = core;
        _twitchLive = core.Settings.Value.TwitchLive;
        _youtubeLive = core.Settings.Value.YouTubeLive;
        core.Twitch.StatsChanged += Twitch_StatsChanged;
        core.Twitch.LiveStateChanged += Twitch_LiveStateChanged;
        core.YouTube.StatsChanged += YouTube_StatsChanged;
        core.YouTube.LiveStateChanged += YouTube_LiveStateChanged;
    }

    public bool IsLive => _twitchLive || _youtubeLive || _core.Settings.Value.TwitchLive || _core.Settings.Value.YouTubeLive;

    public DateTimeOffset? StartedAtUtc
    {
        get
        {
            var candidates = new List<DateTimeOffset>();
            if ((_twitchLive || _core.Settings.Value.TwitchLive) && _twitchStartedAtUtc.HasValue) candidates.Add(_twitchStartedAtUtc.Value);
            if ((_youtubeLive || _core.Settings.Value.YouTubeLive) && _youtubeStartedAtUtc.HasValue) candidates.Add(_youtubeStartedAtUtc.Value);
            return candidates.Count == 0 ? null : candidates.Min();
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            if (!IsLive) return TimeSpan.Zero;
            var start = StartedAtUtc;
            if (!start.HasValue) return TimeSpan.Zero;
            var elapsed = DateTimeOffset.UtcNow - start.Value;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }

    public string DisplayText
    {
        get
        {
            if (!IsLive) return "00:00:00";
            var e = Elapsed;
            var hours = (int)e.TotalHours;
            return $"{hours:00}:{e.Minutes:00}:{e.Seconds:00}";
        }
    }

    private void Twitch_StatsChanged(object? sender, StreamLiveInfo e) => ApplyTwitch(e);
    private void Twitch_LiveStateChanged(object? sender, StreamLiveInfo e) => ApplyTwitch(e);
    private void YouTube_StatsChanged(object? sender, StreamLiveInfo e) => ApplyYouTube(e);
    private void YouTube_LiveStateChanged(object? sender, StreamLiveInfo e) => ApplyYouTube(e);

    private void ApplyTwitch(StreamLiveInfo info)
    {
        _twitchLive = info.IsLive;
        if (!info.IsLive)
        {
            _twitchStartedAtUtc = null;
            return;
        }
        _twitchStartedAtUtc = info.StartedAtUtc ?? _twitchStartedAtUtc ?? DateTimeOffset.UtcNow;
    }

    private void ApplyYouTube(StreamLiveInfo info)
    {
        _youtubeLive = info.IsLive;
        if (!info.IsLive)
        {
            _youtubeStartedAtUtc = null;
            return;
        }
        // YouTubeService already uses its normal live/stat polling. If an exact start
        // timestamp becomes available it wins; otherwise remember the first observed LIVE.
        _youtubeStartedAtUtc = info.StartedAtUtc ?? _youtubeStartedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public void Dispose()
    {
        _core.Twitch.StatsChanged -= Twitch_StatsChanged;
        _core.Twitch.LiveStateChanged -= Twitch_LiveStateChanged;
        _core.YouTube.StatsChanged -= YouTube_StatsChanged;
        _core.YouTube.LiveStateChanged -= YouTube_LiveStateChanged;
    }
}
