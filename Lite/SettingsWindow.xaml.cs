using System.Diagnostics;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public partial class SettingsWindow : Window
{
    private readonly LiteCoreService _core;

    public SettingsWindow()
    {
        InitializeComponent();
        _core = LiteApp.Core;
        LoadValues();
    }

    private void LoadValues()
    {
        var s = _core.Settings.Value;
        var p = _core.Preferences.Value;
        SlClientId.Text = p.StreamlabsClientId;
        SlAuto.IsChecked = p.StreamlabsAutoConnect;
        SlStatus.Text = _core.Streamlabs.Status;
        TwChannel.Text = s.TwitchChannelName;
        TwClientId.Text = s.TwitchClientId;
        TwAuto.IsChecked = s.TwitchAutoConnect;
        YtClientId.Text = s.YouTubeClientId;
        YtAuto.IsChecked = s.YouTubeAutoConnect;
        DonatelloEnabled.IsChecked = s.DonatelloEnabled;

        AimpOverlayEnabled.IsChecked = p.AimpStreamOverlayEnabled;
        AimpOverlayPort.Text = p.AimpStreamOverlayPort.ToString();
        RefreshAimpUrl();
    }

    private void SaveValues()
    {
        var s = _core.Settings.Value;
        var p = _core.Preferences.Value;
        p.StreamlabsClientId = SlClientId.Text.Trim();
        p.StreamlabsAutoConnect = SlAuto.IsChecked == true;
        s.TwitchChannelName = TwChannel.Text.Trim();
        s.TwitchClientId = TwClientId.Text.Trim();
        s.TwitchAutoConnect = TwAuto.IsChecked == true;
        s.YouTubeClientId = YtClientId.Text.Trim();
        s.YouTubeAutoConnect = YtAuto.IsChecked == true;
        s.DonatelloEnabled = DonatelloEnabled.IsChecked == true;
        s.DonatelloAutoStart = s.DonatelloEnabled;

        p.AimpStreamOverlayEnabled = AimpOverlayEnabled.IsChecked == true;
        if (int.TryParse(AimpOverlayPort.Text.Trim(), out var port))
        {
            port = Math.Clamp(port, 1025, 65520);
            if (port is 17846 or 17847 or 17848) port = 17849;
            p.AimpStreamOverlayPort = port;
        }
        AimpOverlayPort.Text = p.AimpStreamOverlayPort.ToString();

        if (SlSecret.Password.Length > 0) _core.Streamlabs.SaveClientSecret(SlSecret.Password);
        if (TwSecret.Password.Length > 0) _core.Credentials.SaveSecret("TWITCH_CLIENT_SECRET", TwSecret.Password);
        if (YtSecret.Password.Length > 0) _core.Credentials.SaveSecret("YOUTUBE_CLIENT_SECRET", YtSecret.Password);
        if (DonatelloToken.Password.Length > 0) _core.Donatello.SaveApiToken(DonatelloToken.Password);

        _core.SettingsService.Save(s);
        _core.Preferences.Save();
        RefreshAimpUrl();
    }

    private async void SlAuthorize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.Streamlabs.AuthorizeAsync(SlClientId.Text.Trim(), SlSecret.Password);
            SlStatus.Text = _core.Streamlabs.Status;
            StatusText.Text = "Streamlabs авторизовано";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void SlConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.Streamlabs.ConnectAsync();
            SlStatus.Text = _core.Streamlabs.Status;
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void SlTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _core.Streamlabs.SendTestAlertAsync("follow");
            StatusText.Text = "Streamlabs test alert надіслано";
        }
        catch (Exception ex) { Error(ex); }
    }

    private void SlForget_Click(object sender, RoutedEventArgs e)
    {
        _core.Streamlabs.ForgetAuthorization();
        SlStatus.Text = _core.Streamlabs.Status;
    }

    private void SlDeveloper_Click(object sender, RoutedEventArgs e) => StreamlabsService.OpenDeveloperPage();

    private async void TwAuthorize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            var secret = TwSecret.Password.Length > 0 ? TwSecret.Password : _core.Credentials.LoadSecret("TWITCH_CLIENT_SECRET");
            await _core.Twitch.AuthorizeAsync(TwClientId.Text.Trim(), secret);
            StatusText.Text = "Twitch підключено";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void YtAuthorize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            var secret = YtSecret.Password.Length > 0 ? YtSecret.Password : _core.Credentials.LoadSecret("YOUTUBE_CLIENT_SECRET");
            await _core.YouTube.AuthorizeAsync(YtClientId.Text.Trim(), secret);
            StatusText.Text = "YouTube підключено";
        }
        catch (Exception ex) { Error(ex); }
    }

    private void SaveDonatello_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            StatusText.Text = "Donatello token збережено";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void StartDonatello_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.Donatello.StartAsync();
            StatusText.Text = _core.Donatello.Status;
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void AimpApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.EnsureAimpStreamOverlayAsync();
            RefreshAimpUrl();
            StatusText.Text = _core.AimpStreamOverlay?.IsRunning == true
                ? $"AIMP overlay працює на порту {_core.AimpStreamOverlay.Port}"
                : "AIMP overlay вимкнено";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void AimpSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.EnsureAimpStreamOverlayAsync();
            var url = _core.AimpStreamOverlay?.SettingsUrl;
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("AIMP overlay вимкнено. Увімкни його та натисни «ЗАПУСТИТИ / ОНОВИТИ».");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            RefreshAimpUrl();
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void AimpPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.EnsureAimpStreamOverlayAsync();
            var url = _core.AimpStreamOverlay?.OverlayUrl;
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("AIMP overlay вимкнено.");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            RefreshAimpUrl();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void RefreshAimpUrl()
    {
        var port = _core.AimpStreamOverlay?.IsRunning == true
            ? _core.AimpStreamOverlay.Port
            : _core.Preferences.Value.AimpStreamOverlayPort;
        AimpOverlayUrl.Text = $"http://127.0.0.1:{port}/overlay/now-playing";
    }

    private void OpenDiscordFull_Click(object sender, RoutedEventArgs e) => new DiscordBotWindow { Owner = this }.ShowDialog();

    private void OpenOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        Window moduleOwner = Owner is MainWindow main ? main : this;
        var w = new GameOverlaySettingsWindow { Owner = moduleOwner };
        w.ShowDialog();
    }

    private void OpenChatBot_Click(object sender, RoutedEventArgs e) => new ChatBotWindow { Owner = this }.ShowDialog();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            await _core.EnsureAimpStreamOverlayAsync();
            RefreshAimpUrl();
            StatusText.Text = "Збережено";
        }
        catch (Exception ex) { Error(ex); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Error(Exception ex) => StatusText.Text = ex.GetBaseException().Message;
}
