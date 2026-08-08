using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public partial class DiscordProfileWindow : Window
{
    private readonly LiteCoreService _core;
    private readonly string _serverId;
    private readonly string _serverName;
    private DiscordServerProfile _profile;

    public DiscordProfileWindow(string serverId, string serverName)
    {
        InitializeComponent();
        _core = LiteApp.Core;
        _serverId = serverId;
        _serverName = serverName;
        _profile = _core.Discord.Profiles.Find(serverId) ?? new DiscordServerProfile
        {
            ServerId = serverId,
            ServerName = serverName,
            StreamMention = _core.Settings.Value.DiscordMention,
            StreamTemplate = _core.Settings.Value.DiscordMessageTemplate,
            MonetizationMention = _core.Settings.Value.DiscordMonetizationMention
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
        StreamChannelsText.Text = _profile.StreamChannelIds.Count == 0 ? "Канали стрімів: не вибрано" : "Канали стрімів: " + string.Join(", ", _profile.StreamChannelIds);
        DonationChannelsText.Text = _profile.MonetizationChannelIds.Count == 0 ? "Канали донатів: не вибрано" : "Канали донатів: " + string.Join(", ", _profile.MonetizationChannelIds);
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
        _core.Discord.Profiles.Update(_profile);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveProfile(); StatusText.Text = "Профіль сервера збережено"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void TestStream_Click(object sender, RoutedEventArgs e)
    {
        try { SaveProfile(); await _core.Discord.TestProfileStreamsAsync(_serverId); StatusText.Text = "Тест стріму надіслано тільки на цей сервер"; }
        catch (Exception ex) { Error(ex); }
    }

    private async void TestDonation_Click(object sender, RoutedEventArgs e)
    {
        try { SaveProfile(); await _core.Discord.TestProfileMonetizationAsync(_serverId); StatusText.Text = "Тест донатів надіслано тільки на цей сервер"; }
        catch (Exception ex) { Error(ex); }
    }

    private void Error(Exception ex)
    {
        _core.Logger.Error("Discord профіль", ex);
        StatusText.Text = ex.GetBaseException().Message;
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
