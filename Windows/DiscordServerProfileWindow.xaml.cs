using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter.Windows;

public partial class DiscordServerProfileWindow : ModuleWindowBase
{
    private readonly AppServices _services = App.Services;
    private readonly string _serverId;
    private readonly string _serverName;
    private DiscordServerProfile _profile;

    public DiscordServerProfileWindow(string serverId, string serverName)
    {
        _serverId = serverId;
        _serverName = serverName;
        InitializeComponent();
        ConfigureModule(DesignSurface, 940, 720, "DiscordServerProfile");

        _profile = _services.Discord.Profiles.Find(serverId) ?? new DiscordServerProfile
        {
            ServerId = serverId,
            ServerName = serverName,
            StreamMention = _services.Settings.Value.DiscordMention,
            StreamTemplate = _services.Settings.Value.DiscordMessageTemplate,
            MonetizationMention = _services.Settings.Value.DiscordMonetizationMention
        };

        LoadProfile();
    }

    private void LoadProfile()
    {
        ServerTitleText.Text = $"DISCORD • {_serverName}";
        ServerIdText.Text = $"Server ID: {_serverId}";
        EnabledCheck.IsChecked = _profile.Enabled;
        StreamMentionBox.Text = _profile.StreamMention;
        StreamTemplateBox.Text = string.IsNullOrWhiteSpace(_profile.StreamTemplate)
            ? "🔴 {platform}: трансляція почалася!\n{title}\n{url}"
            : _profile.StreamTemplate;
        DonationMentionBox.Text = _profile.MonetizationMention;
        DonationTemplateBox.Text = string.IsNullOrWhiteSpace(_profile.MonetizationTemplate)
            ? "⭐ {user}: {kind}\n{amount} {currency}\n{message}"
            : _profile.MonetizationTemplate;
        StreamChannelsText.Text = _profile.StreamChannelIds.Count == 0
            ? "Канали стрімів: не вибрано"
            : "Канали стрімів: " + string.Join(", ", _profile.StreamChannelIds);
        DonationChannelsText.Text = _profile.MonetizationChannelIds.Count == 0
            ? "Канали донатів: не вибрано"
            : "Канали донатів: " + string.Join(", ", _profile.MonetizationChannelIds);
    }

    private void SaveProfile()
    {
        _profile.ServerId = _serverId;
        _profile.ServerName = _serverName;
        _profile.Enabled = EnabledCheck.IsChecked == true;
        _profile.StreamMention = StreamMentionBox.Text.Trim();
        _profile.StreamTemplate = StreamTemplateBox.Text.Trim();
        _profile.MonetizationMention = DonationMentionBox.Text.Trim();
        _profile.MonetizationTemplate = DonationTemplateBox.Text.Trim();
        _services.Discord.Profiles.Update(_profile);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveProfile();
            StatusText.Text = "Профіль сервера збережено.";
        }
        catch (Exception ex) { ShowError("Discord профіль", ex); }
    }

    private async void TestStream_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveProfile();
            await _services.Discord.TestProfileStreamsAsync(_serverId);
            StatusText.Text = "Тест стріму надіслано тільки на цей сервер.";
        }
        catch (Exception ex) { ShowError("Тест стріму", ex); }
    }

    private async void TestDonation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveProfile();
            await _services.Discord.TestProfileMonetizationAsync(_serverId);
            StatusText.Text = "Тест донатів надіслано тільки на цей сервер.";
        }
        catch (Exception ex) { ShowError("Тест донатів", ex); }
    }

    private void ShowError(string title, Exception ex)
    {
        _services.Logger.Error(title, ex);
        StatusText.Text = ex.GetBaseException().Message;
        MessageBox.Show(this, ex.GetBaseException().Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragTitle(sender, e);
    private void Minimize_Click(object sender, RoutedEventArgs e) => MinimizeWindow(sender, e);
    private void Maximize_Click(object sender, RoutedEventArgs e) => MaximizeWindow(sender, e);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseWindow(sender, e);
}
