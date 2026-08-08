namespace TiHiY.StreamControlCenter;

public sealed class LiteEvent
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Platform { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Accent { get; set; } = "#43CDFF";
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayTime => Time.ToString("HH:mm:ss");
    public string Title => string.IsNullOrWhiteSpace(User) ? $"{Platform} • {Type}" : $"{Platform} • {Type} • {User}";
}

public sealed class LiteStats : INotifyPropertyChanged
{
    private int _twitchViewers, _youtubeViewers, _youtubeLikes;
    public int TwitchViewers { get => _twitchViewers; set { if (_twitchViewers == value) return; _twitchViewers = value; Changed(); Changed(nameof(TotalViewers)); } }
    public int YouTubeViewers { get => _youtubeViewers; set { if (_youtubeViewers == value) return; _youtubeViewers = value; Changed(); Changed(nameof(TotalViewers)); } }
    public int YouTubeLikes { get => _youtubeLikes; set { if (_youtubeLikes == value) return; _youtubeLikes = value; Changed(); } }
    public int TotalViewers => TwitchViewers + YouTubeViewers;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
