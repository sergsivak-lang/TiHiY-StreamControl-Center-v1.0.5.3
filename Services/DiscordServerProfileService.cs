using System.Text.Json;

namespace TiHiY.StreamControlCenter.Services;

public sealed class DiscordServerProfile
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public List<string> StreamChannelIds { get; set; } = new();
    public List<string> MonetizationChannelIds { get; set; } = new();
    public string StreamMention { get; set; } = "@everyone";
    public string StreamTemplate { get; set; } = "🔴 {platform}: трансляція почалася!\n{title}\n{url}";
    public string MonetizationMention { get; set; } = string.Empty;
    public string MonetizationTemplate { get; set; } = "⭐ {user}: {kind}\n{amount} {currency}\n{message}";
    public bool Enabled { get; set; } = true;
}

public sealed class DiscordServerProfileService
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly List<DiscordServerProfile> _profiles = new();

    public DiscordServerProfileService(string settingsFolder)
    {
        _file = Path.Combine(settingsFolder, "discord-server-profiles.json");
        Load();
    }

    public IReadOnlyList<DiscordServerProfile> Profiles
    {
        get { lock (_gate) return _profiles.Select(Clone).ToList(); }
    }

    public bool HasStreamTargets => Profiles.Any(x => x.Enabled && x.StreamChannelIds.Count > 0);
    public bool HasMonetizationTargets => Profiles.Any(x => x.Enabled && x.MonetizationChannelIds.Count > 0);

    public DiscordServerProfile GetOrCreate(
        string serverId,
        string serverName,
        string defaultStreamMention = "@everyone",
        string? defaultStreamTemplate = null,
        string defaultMonetizationMention = "")
    {
        lock (_gate)
        {
            var profile = _profiles.FirstOrDefault(x => string.Equals(x.ServerId, serverId, StringComparison.Ordinal));
            if (profile is null)
            {
                profile = new DiscordServerProfile
                {
                    ServerId = serverId,
                    ServerName = serverName,
                    StreamMention = defaultStreamMention,
                    StreamTemplate = string.IsNullOrWhiteSpace(defaultStreamTemplate)
                        ? "🔴 {platform}: трансляція почалася!\n{title}\n{url}"
                        : defaultStreamTemplate,
                    MonetizationMention = defaultMonetizationMention
                };
                _profiles.Add(profile);
            }
            else if (!string.IsNullOrWhiteSpace(serverName))
            {
                profile.ServerName = serverName;
            }
            return profile;
        }
    }

    public DiscordServerProfile? Find(string serverId)
    {
        lock (_gate)
        {
            var profile = _profiles.FirstOrDefault(x => string.Equals(x.ServerId, serverId, StringComparison.Ordinal));
            return profile is null ? null : Clone(profile);
        }
    }

    public void Update(DiscordServerProfile profile)
    {
        lock (_gate)
        {
            var index = _profiles.FindIndex(x => string.Equals(x.ServerId, profile.ServerId, StringComparison.Ordinal));
            if (index < 0) _profiles.Add(Clone(profile));
            else _profiles[index] = Clone(profile);
            SaveLocked();
        }
    }

    public void Save()
    {
        lock (_gate) SaveLocked();
    }

    public void SynchronizeChannels(
        IEnumerable<(string ServerId, string ServerName, string ChannelId, bool Stream, bool Monetization)> selections,
        string defaultStreamMention,
        string defaultStreamTemplate,
        string defaultMonetizationMention)
    {
        lock (_gate)
        {
            var rows = selections.ToList();
            foreach (var group in rows.GroupBy(x => x.ServerId, StringComparer.Ordinal))
            {
                var name = group.Select(x => x.ServerName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? group.Key;
                var profile = _profiles.FirstOrDefault(x => string.Equals(x.ServerId, group.Key, StringComparison.Ordinal));
                if (profile is null)
                {
                    profile = new DiscordServerProfile
                    {
                        ServerId = group.Key,
                        ServerName = name,
                        StreamMention = defaultStreamMention,
                        StreamTemplate = string.IsNullOrWhiteSpace(defaultStreamTemplate)
                            ? "🔴 {platform}: трансляція почалася!\n{title}\n{url}"
                            : defaultStreamTemplate,
                        MonetizationMention = defaultMonetizationMention
                    };
                    _profiles.Add(profile);
                }

                profile.ServerName = name;
                profile.StreamChannelIds = group.Where(x => x.Stream).Select(x => x.ChannelId)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
                profile.MonetizationChannelIds = group.Where(x => x.Monetization).Select(x => x.ChannelId)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
            }
            SaveLocked();
        }
    }

    private void Load()
    {
        lock (_gate)
        {
            _profiles.Clear();
            try
            {
                if (!File.Exists(_file)) return;
                var loaded = JsonSerializer.Deserialize<List<DiscordServerProfile>>(File.ReadAllText(_file), _json);
                if (loaded is null) return;
                foreach (var item in loaded)
                {
                    if (string.IsNullOrWhiteSpace(item.ServerId)) continue;
                    item.StreamChannelIds ??= new List<string>();
                    item.MonetizationChannelIds ??= new List<string>();
                    _profiles.Add(item);
                }
            }
            catch { }
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_profiles, _json));
        File.Move(temp, _file, true);
    }

    private static DiscordServerProfile Clone(DiscordServerProfile x) => new()
    {
        ServerId = x.ServerId,
        ServerName = x.ServerName,
        StreamChannelIds = x.StreamChannelIds?.ToList() ?? new List<string>(),
        MonetizationChannelIds = x.MonetizationChannelIds?.ToList() ?? new List<string>(),
        StreamMention = x.StreamMention,
        StreamTemplate = x.StreamTemplate,
        MonetizationMention = x.MonetizationMention,
        MonetizationTemplate = x.MonetizationTemplate,
        Enabled = x.Enabled
    };
}
