using System.Collections.ObjectModel;
using TiHiY.StreamControlCenter.Models;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public partial class DiscordServersWindow : Window
{
    private readonly LiteCoreService _core;
    private readonly ObservableCollection<ChannelSelectionRow> _visibleRows = new();
    private readonly List<ChannelSelectionRow> _allRows = new();
    private bool _loadedOnce;

    public DiscordServersWindow()
    {
        InitializeComponent();
        _core = LiteApp.Core;
        ChannelGrid.ItemsSource = _visibleRows;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        if (Environment.GetCommandLineArgs().Any(x => x.StartsWith("--ci-ui-audit=", StringComparison.OrdinalIgnoreCase)))
        {
            LoadCiDemo();
            return;
        }
        await RefreshServersAsync();
    }

    private void LoadCiDemo()
    {
        _allRows.Clear();
        _allRows.AddRange(new[]
        {
            Demo("1001", "Cobra Team Six", "2001", "Gateway", "welcome", true, false),
            Demo("1001", "Cobra Team Six", "2002", "Announcements", "announcements", true, true),
            Demo("1002", "TiHiY Community", "3001", "Streams", "live-now", true, false),
            Demo("1003", "Partner Server", "4001", "Support", "donations", false, true)
        });
        ApplyFilter();
        StatusText.Text = "Знайдено серверів: 3 • каналів: 4 • готові: 4.";
    }

    private static ChannelSelectionRow Demo(string serverId, string server, string channelId, string category, string channel, bool stream, bool donation) => new()
    {
        Info = new DiscordServerChannelInfo
        {
            ServerId = serverId,
            ServerName = server,
            ChannelId = channelId,
            ChannelName = channel,
            CategoryName = category,
            ChannelType = 0,
            Position = 0,
            CanView = true,
            CanSend = true,
            CanEmbedLinks = true,
            CanMentionEveryone = true
        },
        StreamSelected = stream,
        MonetizationSelected = donation
    };

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshServersAsync();

    private async Task RefreshServersAsync()
    {
        try
        {
            StatusText.Text = "Підключаюся до Discord Gateway…";
            ChannelGrid.IsEnabled = false;
            var discovered = await DiscordServerDiscoveryService.DiscoverAsync(_core.Discord.LoadBotToken());

            var streamIds = ParseIds(_core.Settings.Value.DiscordChannelIds);
            var donationIds = ParseIds(_core.Settings.Value.DiscordMonetizationChannelIds);
            foreach (var profile in _core.Discord.Profiles.Profiles)
            {
                streamIds.UnionWith(profile.StreamChannelIds);
                donationIds.UnionWith(profile.MonetizationChannelIds);
            }

            _allRows.Clear();
            _allRows.AddRange(discovered.Select(x => new ChannelSelectionRow
            {
                Info = x,
                StreamSelected = streamIds.Contains(x.ChannelId),
                MonetizationSelected = donationIds.Contains(x.ChannelId)
            }));
            ApplyFilter();

            var servers = _allRows.Select(x => x.ServerId).Distinct(StringComparer.Ordinal).Count();
            StatusText.Text = $"Знайдено серверів: {servers} • каналів: {_allRows.Count} • готові: {_allRows.Count(x => x.IsUsable)}.";
        }
        catch (Exception ex) { Error(ex); }
        finally { ChannelGrid.IsEnabled = true; }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = (ChannelGrid.SelectedItem as ChannelSelectionRow)?.ChannelId;
        _visibleRows.Clear();
        var usableOnly = UsableOnlyCheck.IsChecked == true;
        foreach (var row in _allRows)
            if (!usableOnly || row.IsUsable) _visibleRows.Add(row);
        if (!string.IsNullOrWhiteSpace(selected)) ChannelGrid.SelectedItem = _visibleRows.FirstOrDefault(x => x.ChannelId == selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveSelection(); StatusText.Text = "Канали та профілі Discord збережено"; }
        catch (Exception ex) { Error(ex); }
    }

    private void SaveSelection()
    {
        ChannelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ChannelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var invalid = _allRows.Where(x => (x.StreamSelected || x.MonetizationSelected) && !x.IsUsable).ToList();
        if (invalid.Count > 0)
        {
            var names = string.Join(Environment.NewLine, invalid.Take(8).Select(x => $"{x.ServerName} / #{x.ChannelName}: {x.PermissionSummary}"));
            throw new InvalidOperationException("Для вибраних каналів бракує прав бота:\n" + names);
        }

        _core.Settings.Value.DiscordChannelIds = string.Join(Environment.NewLine,
            _allRows.Where(x => x.StreamSelected && x.IsUsable).Select(x => x.ChannelId).Distinct(StringComparer.Ordinal));
        _core.Settings.Value.DiscordMonetizationChannelIds = string.Join(Environment.NewLine,
            _allRows.Where(x => x.MonetizationSelected && x.IsUsable).Select(x => x.ChannelId).Distinct(StringComparer.Ordinal));

        _core.Discord.Profiles.SynchronizeChannels(
            _allRows.Select(x => (x.ServerId, x.ServerName, x.ChannelId, x.StreamSelected && x.IsUsable, x.MonetizationSelected && x.IsUsable)),
            _core.Settings.Value.DiscordMention,
            _core.Settings.Value.DiscordMessageTemplate,
            _core.Settings.Value.DiscordMonetizationMention);
        _core.SettingsService.Save(_core.Settings.Value);
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ChannelGrid.SelectedItem is not ChannelSelectionRow row)
            {
                StatusText.Text = "Спочатку виділіть канал потрібного сервера";
                return;
            }
            SaveSelection();
            new DiscordProfileWindow(row.ServerId, row.ServerName) { Owner = this }.ShowDialog();
            StatusText.Text = $"Профіль «{row.ServerName}» збережено окремо";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void TestStreams_Click(object sender, RoutedEventArgs e)
    {
        try { SaveSelection(); await _core.Discord.TestAsync(); StatusText.Text = "Тест стрімів надіслано"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void TestDonations_Click(object sender, RoutedEventArgs e)
    {
        try { SaveSelection(); await _core.Discord.TestMonetizationAsync(); StatusText.Text = "Тест донатів надіслано"; }
        catch (Exception ex) { Error(ex); }
    }

    private static HashSet<string> ParseIds(string text) => (text ?? string.Empty)
        .Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    private void Error(Exception ex)
    {
        _core.Logger.Error("Discord сервери", ex);
        StatusText.Text = ex.GetBaseException().Message;
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class ChannelSelectionRow
    {
        public DiscordServerChannelInfo Info { get; init; } = new();
        public string ServerId => Info.ServerId;
        public string ServerName => Info.ServerName;
        public string ChannelId => Info.ChannelId;
        public string ChannelName => Info.ChannelName;
        public string DisplayChannel => Info.DisplayChannel;
        public string PermissionSummary => Info.PermissionSummary;
        public bool IsUsable => Info.IsUsable;
        public bool StreamSelected { get; set; }
        public bool MonetizationSelected { get; set; }
    }
}
