using System.Collections.ObjectModel;
using System.Diagnostics;
using TiHiY.StreamControlCenter.Models;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter.Windows;

public partial class DiscordServersWindow : ModuleWindowBase
{
    private readonly AppServices _services = App.Services;
    private readonly ObservableCollection<ChannelSelectionRow> _visibleRows = new();
    private readonly List<ChannelSelectionRow> _allRows = new();
    private bool _loadedOnce;

    public DiscordServersWindow()
    {
        InitializeComponent();
        ConfigureModule(DesignSurface, 1180, 740, "DiscordServers");
        ChannelGrid.ItemsSource = _visibleRows;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await RefreshServersAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshServersAsync();

    private async Task RefreshServersAsync()
    {
        try
        {
            StatusText.Text = "Підключаюся до Discord Gateway…";
            ChannelGrid.IsEnabled = false;
            var token = _services.Discord.LoadBotToken();
            var discovered = await DiscordServerDiscoveryService.DiscoverAsync(token);
            var streamIds = ParseIds(_services.Settings.Value.DiscordChannelIds);
            var donationIds = ParseIds(_services.Settings.Value.DiscordMonetizationChannelIds);

            _allRows.Clear();
            _allRows.AddRange(discovered.Select(x => new ChannelSelectionRow
            {
                Info = x,
                StreamSelected = streamIds.Contains(x.ChannelId),
                MonetizationSelected = donationIds.Contains(x.ChannelId)
            }));
            ApplyFilter();

            var servers = _allRows.Select(x => x.ServerId).Distinct(StringComparer.Ordinal).Count();
            var usable = _allRows.Count(x => x.IsUsable);
            StatusText.Text = $"Знайдено серверів: {servers} • текстових каналів: {_allRows.Count} • готові до сповіщень: {usable}.";
        }
        catch (Exception ex)
        {
            ShowError("Discord сервери", ex);
        }
        finally
        {
            ChannelGrid.IsEnabled = true;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _visibleRows.Clear();
        var usableOnly = UsableOnlyCheck.IsChecked == true;
        foreach (var row in _allRows)
            if (!usableOnly || row.IsUsable)
                _visibleRows.Add(row);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSelection();
            StatusText.Text = "Вибір Discord-каналів збережено.";
        }
        catch (Exception ex) { ShowError("Збереження Discord-каналів", ex); }
    }

    private void SaveSelection()
    {
        var invalid = _allRows.Where(x => (x.StreamSelected || x.MonetizationSelected) && !x.IsUsable).ToList();
        if (invalid.Count > 0)
        {
            var names = string.Join("\n", invalid.Take(8).Select(x => $"{x.ServerName} / #{x.ChannelName}: {x.PermissionSummary}"));
            throw new InvalidOperationException("Для вибраних каналів бракує прав бота:\n" + names);
        }

        _services.Settings.Value.DiscordChannelIds = string.Join(Environment.NewLine,
            _allRows.Where(x => x.StreamSelected && x.IsUsable).Select(x => x.ChannelId).Distinct(StringComparer.Ordinal));
        _services.Settings.Value.DiscordMonetizationChannelIds = string.Join(Environment.NewLine,
            _allRows.Where(x => x.MonetizationSelected && x.IsUsable).Select(x => x.ChannelId).Distinct(StringComparer.Ordinal));
        _services.Save();
    }

    private async void TestStreams_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSelection();
            await _services.Discord.TestAsync();
            StatusText.Text = "Тест стрімів надіслано в усі вибрані канали.";
        }
        catch (Exception ex) { ShowError("Тест каналів стрімів", ex); }
    }

    private async void TestDonations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSelection();
            await _services.Discord.TestMonetizationAsync();
            StatusText.Text = "Тест донатів надіслано в усі вибрані канали.";
        }
        catch (Exception ex) { ShowError("Тест каналів донатів", ex); }
    }

    private void InviteBot_Click(object sender, RoutedEventArgs e)
    {
        var appId = _services.Settings.Value.DiscordApplicationId.Trim();
        if (string.IsNullOrWhiteSpace(appId))
        {
            MessageBox.Show(this, "Спочатку вкажіть Discord Application ID у вікні «Сповіщення» та збережіть його.", "Discord", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var url = $"https://discord.com/oauth2/authorize?client_id={Uri.EscapeDataString(appId)}&scope=bot&permissions=274877975552";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelGrid.SelectedItem is not ChannelSelectionRow row) return;
        Process.Start(new ProcessStartInfo($"https://discord.com/channels/{row.ServerId}/{row.ChannelId}") { UseShellExecute = true });
    }

    private static HashSet<string> ParseIds(string text) => text
        .Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    private void ShowError(string title, Exception ex)
    {
        _services.Logger.Error(title, ex);
        var message = ex.GetBaseException().Message;
        StatusText.Text = message;
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragTitle(sender, e);
    private void Minimize_Click(object sender, RoutedEventArgs e) => MinimizeWindow(sender, e);
    private void Maximize_Click(object sender, RoutedEventArgs e) => MaximizeWindow(sender, e);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseWindow(sender, e);

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
