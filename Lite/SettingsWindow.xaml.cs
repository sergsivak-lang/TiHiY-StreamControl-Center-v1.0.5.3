using System.Globalization;
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
        var s = _core.Settings.Value; var p = _core.Preferences.Value;
        SlClientId.Text = p.StreamlabsClientId; SlAuto.IsChecked = p.StreamlabsAutoConnect; SlStatus.Text = _core.Streamlabs.Status;
        TwChannel.Text = s.TwitchChannelName; TwClientId.Text = s.TwitchClientId; TwAuto.IsChecked = s.TwitchAutoConnect;
        YtClientId.Text = s.YouTubeClientId; YtAuto.IsChecked = s.YouTubeAutoConnect;
        DonatelloEnabled.IsChecked = s.DonatelloEnabled;
        DiscordAppId.Text = s.DiscordApplicationId; DiscordChannels.Text = s.DiscordChannelIds; DiscordEnabled.IsChecked = s.DiscordNotificationsEnabled; DiscordMoney.IsChecked = s.DiscordMonetizationEnabled;
        HudAuto.IsChecked = p.HudAutoStart; HudClick.IsChecked = p.HudClickThrough; HudCapture.IsChecked = p.HudExcludeFromCapture; HudEvents.IsChecked = p.HudShowEvents; HudStats.IsChecked = p.HudShowStats; HudAimp.IsChecked = p.HudShowAimp;
        HudOpacity.Text = p.HudOpacity.ToString("0.00", CultureInfo.InvariantCulture); HudBg.Text = p.HudBackgroundOpacity.ToString("0.00", CultureInfo.InvariantCulture); HudFont.Text = p.HudFontSize.ToString("0.#", CultureInfo.InvariantCulture); HudCount.Text = p.HudMaxMessages.ToString(CultureInfo.InvariantCulture);
    }

    private void SaveValues()
    {
        var s = _core.Settings.Value; var p = _core.Preferences.Value;
        p.StreamlabsClientId = SlClientId.Text.Trim(); p.StreamlabsAutoConnect = SlAuto.IsChecked == true;
        s.TwitchChannelName = TwChannel.Text.Trim(); s.TwitchClientId = TwClientId.Text.Trim(); s.TwitchAutoConnect = TwAuto.IsChecked == true;
        s.YouTubeClientId = YtClientId.Text.Trim(); s.YouTubeAutoConnect = YtAuto.IsChecked == true;
        s.DonatelloEnabled = DonatelloEnabled.IsChecked == true; s.DonatelloAutoStart = s.DonatelloEnabled;
        s.DiscordApplicationId = DiscordAppId.Text.Trim(); s.DiscordChannelIds = DiscordChannels.Text.Trim(); s.DiscordNotificationsEnabled = DiscordEnabled.IsChecked == true; s.NotificationBotAutoStart = s.DiscordNotificationsEnabled; s.DiscordMonetizationEnabled = DiscordMoney.IsChecked == true;
        p.HudAutoStart = HudAuto.IsChecked == true; p.HudClickThrough = HudClick.IsChecked == true; p.HudExcludeFromCapture = HudCapture.IsChecked == true; p.HudShowEvents = HudEvents.IsChecked == true; p.HudShowStats = HudStats.IsChecked == true; p.HudShowAimp = HudAimp.IsChecked == true;
        p.HudOpacity = ParseDouble(HudOpacity.Text, p.HudOpacity, .2, 1); p.HudBackgroundOpacity = ParseDouble(HudBg.Text, p.HudBackgroundOpacity, 0, 1); p.HudFontSize = ParseDouble(HudFont.Text, p.HudFontSize, 11, 32); p.HudMaxMessages = ParseInt(HudCount.Text, p.HudMaxMessages, 1, 30);
        if (SlSecret.Password.Length > 0) _core.Streamlabs.SaveClientSecret(SlSecret.Password);
        if (TwSecret.Password.Length > 0) _core.Credentials.SaveSecret("TWITCH_CLIENT_SECRET", TwSecret.Password);
        if (YtSecret.Password.Length > 0) _core.Credentials.SaveSecret("YOUTUBE_CLIENT_SECRET", YtSecret.Password);
        if (DonatelloToken.Password.Length > 0) _core.Donatello.SaveApiToken(DonatelloToken.Password);
        if (DiscordToken.Password.Length > 0) _core.Discord.SaveBotToken(DiscordToken.Password);
        _core.SettingsService.Save(s); _core.Preferences.Save();
    }

    private async void SlAuthorize_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); await _core.Streamlabs.AuthorizeAsync(SlClientId.Text.Trim(), SlSecret.Password); SlStatus.Text = _core.Streamlabs.Status; StatusText.Text = "Streamlabs авторизовано"; }
        catch (Exception ex) { Error(ex); }
    }
    private async void SlConnect_Click(object sender, RoutedEventArgs e) { try { SaveValues(); await _core.Streamlabs.ConnectAsync(); SlStatus.Text = _core.Streamlabs.Status; } catch (Exception ex) { Error(ex); } }
    private async void SlTest_Click(object sender, RoutedEventArgs e) { try { await _core.Streamlabs.SendTestAlertAsync("follow"); StatusText.Text = "Streamlabs test alert надіслано"; } catch (Exception ex) { Error(ex); } }
    private void SlForget_Click(object sender, RoutedEventArgs e) { _core.Streamlabs.ForgetAuthorization(); SlStatus.Text = _core.Streamlabs.Status; }
    private void SlDeveloper_Click(object sender, RoutedEventArgs e) => StreamlabsService.OpenDeveloperPage();

    private async void TwAuthorize_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); var secret = TwSecret.Password.Length > 0 ? TwSecret.Password : _core.Credentials.LoadSecret("TWITCH_CLIENT_SECRET"); await _core.Twitch.AuthorizeAsync(TwClientId.Text.Trim(), secret); StatusText.Text = "Twitch підключено"; }
        catch (Exception ex) { Error(ex); }
    }
    private async void YtAuthorize_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); var secret = YtSecret.Password.Length > 0 ? YtSecret.Password : _core.Credentials.LoadSecret("YOUTUBE_CLIENT_SECRET"); await _core.YouTube.AuthorizeAsync(YtClientId.Text.Trim(), secret); StatusText.Text = "YouTube підключено"; }
        catch (Exception ex) { Error(ex); }
    }
    private void SaveDonatello_Click(object sender, RoutedEventArgs e) { try { SaveValues(); StatusText.Text = "Donatello token збережено"; } catch (Exception ex) { Error(ex); } }
    private async void StartDonatello_Click(object sender, RoutedEventArgs e) { try { SaveValues(); await _core.Donatello.StartAsync(); StatusText.Text = _core.Donatello.Status; } catch (Exception ex) { Error(ex); } }
    private void SaveDiscord_Click(object sender, RoutedEventArgs e) { try { SaveValues(); StatusText.Text = "Discord налаштування збережено"; } catch (Exception ex) { Error(ex); } }
    private async void StartDiscord_Click(object sender, RoutedEventArgs e) { try { SaveValues(); await _core.NotifyBot.StartAsync(); StatusText.Text = _core.NotifyBot.Status; } catch (Exception ex) { Error(ex); } }
    private async void TestDiscord_Click(object sender, RoutedEventArgs e) { try { SaveValues(); await _core.Discord.TestAsync(); StatusText.Text = "Тест Discord надіслано"; } catch (Exception ex) { Error(ex); } }
    private void DiscordProfiles_Click(object sender, RoutedEventArgs e) { new DiscordProfilesEditorWindow(_core) { Owner = this }.ShowDialog(); }
    private void Save_Click(object sender, RoutedEventArgs e) { try { SaveValues(); StatusText.Text = "Збережено"; } catch (Exception ex) { Error(ex); } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Error(Exception ex) => StatusText.Text = ex.GetBaseException().Message;
    private static double ParseDouble(string s, double fallback, double min, double max) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v,min,max) : fallback;
    private static int ParseInt(string s, int fallback, int min, int max) => int.TryParse(s, out var v) ? Math.Clamp(v,min,max) : fallback;
}
