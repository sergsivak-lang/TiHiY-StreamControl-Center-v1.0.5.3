using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

public partial class ChatBotWindow : Window
{
    private readonly LiteCoreService _core;
    private ScheduledNotice? _selectedNotice;
    private BotCommand? _selectedCommand;

    public ChatBotWindow()
    {
        InitializeComponent();
        _core = LiteApp.Core;
        NoticeList.ItemsSource = _core.ChatBot.Notices;
        CommandList.ItemsSource = _core.ChatBot.Commands;
        LoadSettings();
        _core.ChatBot.StatusChanged += ChatBot_StatusChanged;
        Closed += (_, _) => _core.ChatBot.StatusChanged -= ChatBot_StatusChanged;
        RefreshStatus();
        NewNotice_Click(this, new RoutedEventArgs());
        NewCommand_Click(this, new RoutedEventArgs());
    }

    private void LoadSettings()
    {
        var s = _core.Settings.Value;
        AutoNoticesCheck.IsChecked = s.AutoNoticesEnabled;
        BotEnabledCheck.IsChecked = s.ChatBotEnabled;
        SpamCheck.IsChecked = s.ChatBotSpamProtectionEnabled;
        BlockLinksCheck.IsChecked = s.ChatBotBlockLinks;
        BlockCapsCheck.IsChecked = s.ChatBotBlockCaps;
        BlockRepeatsCheck.IsChecked = s.ChatBotBlockRepeats;
        BlockedWords.Text = s.ChatBotBlockedWords;
    }

    private void SaveGlobalSettings()
    {
        var s = _core.Settings.Value;
        s.AutoNoticesEnabled = AutoNoticesCheck.IsChecked == true;
        s.ChatBotEnabled = BotEnabledCheck.IsChecked == true;
        s.ChatBotSpamProtectionEnabled = SpamCheck.IsChecked == true;
        s.ChatBotBlockLinks = BlockLinksCheck.IsChecked == true;
        s.ChatBotBlockCaps = BlockCapsCheck.IsChecked == true;
        s.ChatBotBlockRepeats = BlockRepeatsCheck.IsChecked == true;
        s.ChatBotBlockedWords = BlockedWords.Text.Trim();
        _core.ChatBot.SaveAll();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        SaveGlobalSettings();
        _core.ChatBot.Start();
        StatusText.Text = "Chat Bot запущено";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _core.ChatBot.Stop();
        StatusText.Text = "Chat Bot зупинено";
    }

    private void ChatBot_StatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshStatus));
    private void RefreshStatus()
    {
        BotStatus.Text = _core.ChatBot.Status;
        BotStatus.Foreground = (Brush)FindResource(_core.ChatBot.IsRunning ? "Green" : "Amber");
    }

    private void AutoNotices_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SaveGlobalSettings();
    }

    private void NoticeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NoticeList.SelectedItem is not ScheduledNotice n) return;
        _selectedNotice = n;
        NoticeName.Text = n.Name;
        NoticeText.Text = n.Text;
        NoticeInterval.Text = n.IntervalMinutes.ToString();
        NoticeMinMessages.Text = n.MinimumChatMessages.ToString();
        NoticeEnabled.IsChecked = n.Enabled;
        SelectCombo(NoticeTarget, n.Target);
    }

    private void NewNotice_Click(object sender, RoutedEventArgs e)
    {
        _selectedNotice = null;
        NoticeList.SelectedItem = null;
        NoticeName.Text = "Нове сповіщення";
        NoticeText.Clear();
        NoticeInterval.Text = "20";
        NoticeMinMessages.Text = "0";
        NoticeEnabled.IsChecked = true;
        NoticeTarget.SelectedIndex = 2;
    }

    private void SaveNotice_Click(object sender, RoutedEventArgs e)
    {
        var n = _selectedNotice ?? new ScheduledNotice();
        n.Name = string.IsNullOrWhiteSpace(NoticeName.Text) ? "Сповіщення" : NoticeName.Text.Trim();
        n.Text = NoticeText.Text.Trim();
        n.Target = ComboText(NoticeTarget);
        n.IntervalMinutes = ParseInt(NoticeInterval.Text, 20, 1, 1440);
        n.MinimumChatMessages = ParseInt(NoticeMinMessages.Text, 0, 0, 10000);
        n.Enabled = NoticeEnabled.IsChecked == true;
        n.NextRun = DateTime.Now.AddMinutes(n.IntervalMinutes);
        if (_selectedNotice is null) _core.ChatBot.Notices.Add(n);
        _selectedNotice = n;
        _core.ChatBot.SaveAll();
        NoticeList.Items.Refresh();
        StatusText.Text = "Автосповіщення збережено";
    }

    private async void SendNotice_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _core.ChatBot.SendNowAsync(NoticeText.Text.Trim(), ComboText(NoticeTarget));
            StatusText.Text = "Сповіщення надіслано";
        }
        catch (Exception ex) { StatusText.Text = ex.GetBaseException().Message; }
    }

    private void DeleteNotice_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNotice is null) return;
        _core.ChatBot.Notices.Remove(_selectedNotice);
        _core.ChatBot.SaveAll();
        NewNotice_Click(sender, e);
        StatusText.Text = "Сповіщення видалено";
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is not BotCommand c) return;
        _selectedCommand = c;
        CommandName.Text = c.Name;
        CommandReply.Text = c.Reply;
        CommandCooldown.Text = c.CooldownSeconds.ToString();
        CommandEnabled.IsChecked = c.Enabled;
        SelectCombo(CommandTarget, c.Target);
    }

    private void NewCommand_Click(object sender, RoutedEventArgs e)
    {
        _selectedCommand = null;
        CommandList.SelectedItem = null;
        CommandName.Text = "!команда";
        CommandReply.Text = "Відповідь бота";
        CommandCooldown.Text = "10";
        CommandEnabled.IsChecked = true;
        CommandTarget.SelectedIndex = 2;
    }

    private void SaveCommand_Click(object sender, RoutedEventArgs e)
    {
        SaveGlobalSettings();
        var c = _selectedCommand ?? new BotCommand();
        var name = string.IsNullOrWhiteSpace(CommandName.Text) ? "!команда" : CommandName.Text.Trim();
        c.Name = name.StartsWith('!') ? name : "!" + name;
        c.Reply = CommandReply.Text.Trim();
        c.Target = ComboText(CommandTarget);
        c.CooldownSeconds = ParseInt(CommandCooldown.Text, 10, 0, 86400);
        c.Enabled = CommandEnabled.IsChecked == true;
        if (_selectedCommand is null) _core.ChatBot.Commands.Add(c);
        _selectedCommand = c;
        _core.ChatBot.SaveAll();
        CommandList.Items.Refresh();
        StatusText.Text = "Команду збережено";
    }

    private async void TestCommand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var reply = CommandReply.Text.Replace("{song}", _core.CurrentSong(), StringComparison.OrdinalIgnoreCase);
            await _core.ChatBot.SendNowAsync(reply, ComboText(CommandTarget));
            StatusText.Text = "Тестову відповідь надіслано";
        }
        catch (Exception ex) { StatusText.Text = ex.GetBaseException().Message; }
    }

    private void DeleteCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCommand is null) return;
        _core.ChatBot.Commands.Remove(_selectedCommand);
        _core.ChatBot.SaveAll();
        NewCommand_Click(sender, e);
        StatusText.Text = "Команду видалено";
    }

    private static string ComboText(ComboBox combo) => combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? string.Empty : combo.Text;
    private static void SelectCombo(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = item; return; }
        combo.SelectedIndex = 2;
    }
    private static int ParseInt(string text, int fallback, int min, int max) => int.TryParse(text, out var v) ? Math.Clamp(v, min, max) : fallback;
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Close_Click(object sender, RoutedEventArgs e) { SaveGlobalSettings(); Close(); }
}
