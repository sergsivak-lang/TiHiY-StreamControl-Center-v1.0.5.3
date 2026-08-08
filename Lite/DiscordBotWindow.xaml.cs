using System.Diagnostics;

namespace TiHiY.StreamControlCenter;

public partial class DiscordBotWindow : Window
{
    private readonly LiteCoreService _core;

    public DiscordBotWindow()
    {
        InitializeComponent();
        _core = LiteApp.Core;
        LoadValues();
        _core.NotifyBot.StatusChanged += NotifyBot_StatusChanged;
        Closed += (_, _) => _core.NotifyBot.StatusChanged -= NotifyBot_StatusChanged;
        RefreshState();
    }

    private void LoadValues()
    {
        var s = _core.Settings.Value;
        ApplicationIdBox.Text = s.DiscordApplicationId;
        ChannelIdsBox.Text = s.DiscordChannelIds;
        MentionBox.Text = s.DiscordMention;
        EnabledCheck.IsChecked = s.DiscordNotificationsEnabled;
        AutoStartCheck.IsChecked = s.NotificationBotAutoStart;
        TwitchCheck.IsChecked = s.DiscordNotifyTwitch;
        YouTubeCheck.IsChecked = s.DiscordNotifyYouTube;
        TemplateBox.Text = s.DiscordMessageTemplate;
        MoneyChannelIdsBox.Text = s.DiscordMonetizationChannelIds;
        MoneyMentionBox.Text = s.DiscordMonetizationMention;
        MoneyEnabledCheck.IsChecked = s.DiscordMonetizationEnabled;
        DonatelloMoneyCheck.IsChecked = s.DiscordNotifyDonatelloMonetization;
        TwitchMoneyCheck.IsChecked = s.DiscordNotifyTwitchMonetization;
        YouTubeMoneyCheck.IsChecked = s.DiscordNotifyYouTubeMonetization;
        ConfigurationStateText.Text = _core.Discord.HasBotToken
            ? "Discord Bot Token збережено у Windows Credential Manager."
            : "Discord Bot Token ще не збережено.";
    }

    private void SaveValues()
    {
        var s = _core.Settings.Value;
        s.DiscordApplicationId = ApplicationIdBox.Text.Trim();
        s.DiscordChannelIds = ChannelIdsBox.Text.Trim();
        s.DiscordMention = MentionBox.Text.Trim();
        s.DiscordNotificationsEnabled = EnabledCheck.IsChecked == true;
        s.NotificationBotAutoStart = AutoStartCheck.IsChecked == true;
        s.DiscordNotifyTwitch = TwitchCheck.IsChecked == true;
        s.DiscordNotifyYouTube = YouTubeCheck.IsChecked == true;
        s.DiscordMessageTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text)
            ? "🔴 {platform}: трансляція почалася!\n{title}\n{url}"
            : TemplateBox.Text.Trim();
        s.DiscordMonetizationChannelIds = MoneyChannelIdsBox.Text.Trim();
        s.DiscordMonetizationMention = MoneyMentionBox.Text.Trim();
        s.DiscordMonetizationEnabled = MoneyEnabledCheck.IsChecked == true;
        s.DiscordNotifyDonatelloMonetization = DonatelloMoneyCheck.IsChecked == true;
        s.DiscordNotifyTwitchMonetization = TwitchMoneyCheck.IsChecked == true;
        s.DiscordNotifyYouTubeMonetization = YouTubeMoneyCheck.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(BotTokenBox.Password))
        {
            _core.Discord.SaveBotToken(BotTokenBox.Password);
            BotTokenBox.Password = string.Empty;
        }
        _core.SettingsService.Save(s);
        ConfigurationStateText.Text = _core.Discord.HasBotToken
            ? "Налаштування збережено. Токен захищено Windows Credential Manager."
            : "Налаштування збережено, але Bot Token відсутній.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); StatusText.Text = "Налаштування Discord збережено"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); await _core.NotifyBot.StartAsync(); RefreshState(); StatusText.Text = "Discord Notify Bot запущено"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        try { await _core.NotifyBot.StopAsync(); RefreshState(); StatusText.Text = "Discord Notify Bot зупинено"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveValues();
            if (!_core.NotifyBot.IsRunning) await _core.NotifyBot.StartAsync();
            else await _core.NotifyBot.CheckNowAsync();
            StatusText.Text = "Перевірку Twitch/YouTube виконано";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async void TestStream_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); await _core.NotifyBot.SendTestAsync(); StatusText.Text = "Тест стріму надіслано"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void TestMoney_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); await _core.Discord.TestMonetizationAsync(); StatusText.Text = "Тест монетизації надіслано"; }
        catch (Exception ex) { Error(ex); }
    }

    private void Servers_Click(object sender, RoutedEventArgs e)
    {
        try { SaveValues(); new DiscordServersWindow { Owner = this }.ShowDialog(); LoadValues(); }
        catch (Exception ex) { Error(ex); }
    }

    private void Invite_Click(object sender, RoutedEventArgs e)
    {
        SaveValues();
        var id = ApplicationIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(id)) { StatusText.Text = "Спочатку вкажіть Application ID"; return; }
        Process.Start(new ProcessStartInfo($"https://discord.com/oauth2/authorize?client_id={Uri.EscapeDataString(id)}&scope=bot&permissions=274877975552") { UseShellExecute = true });
    }

    private void Portal_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true });

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        _core.Discord.ForgetBotToken();
        BotTokenBox.Password = string.Empty;
        ConfigurationStateText.Text = "Discord Bot Token видалено з Windows Credential Manager.";
        StatusText.Text = "Token видалено";
    }

    private void NotifyBot_StatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshState));
    private void RefreshState()
    {
        BotStateText.Text = _core.NotifyBot.Status;
        BotStateText.Foreground = (Brush)FindResource(_core.NotifyBot.IsRunning ? "Green" : "Amber");
    }

    private void Error(Exception ex)
    {
        _core.Logger.Error("Discord Bot", ex);
        StatusText.Text = ex.GetBaseException().Message;
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
