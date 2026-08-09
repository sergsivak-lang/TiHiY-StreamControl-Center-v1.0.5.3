using System.Windows.Documents;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

public partial class MainWindow : Window
{
    private readonly LiteCoreService _core;
    private readonly DispatcherTimer _liveTimer;
    private HudWindow? _hud;

    public MainWindow()
    {
        InitializeComponent();
        _core = LiteApp.Core;
        DataContext = _core;
        _core.ChatAdded += Core_ChatAdded;
        _core.StatusChanged += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshStatus));
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) => RefreshLiveTime();
        Loaded += (_, _) =>
        {
            RefreshStatus();
            RefreshLiveTime();
            _liveTimer.Start();
            foreach (var message in _core.Chat) AddChatRow(message);
            if (_core.Settings.Value.LocalChatOverlayAutoStart) ShowOverlay();
        };
        Closing += (_, _) =>
        {
            _liveTimer.Stop();
            try { _hud?.Close(); } catch { }
        };
    }

    private void Core_ChatAdded(object? sender, ChatMessage e) => Dispatcher.BeginInvoke(new Action(() => AddChatRow(e)));

    private void AddChatRow(ChatMessage m)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(20, 52, 72)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 7, 4, 7),
            Background = m.IsHighlighted ? BrushFrom(m.Background, Brushes.Transparent) : Brushes.Transparent,
            Tag = m
        };
        var row = new DockPanel();
        border.Child = row;

        var icon = LitePlatformVisual.BuildIcon(m.Platform, 25);
        if (icon is FrameworkElement iconElement) iconElement.Margin = new Thickness(0, 1, 8, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        var stack = new StackPanel();
        row.Children.Add(stack);
        var header = new TextBlock();
        header.Inlines.Add(new Run(m.User) { FontWeight = FontWeights.Bold, Foreground = BrushFrom(m.Foreground, (Brush)FindResource("Cyan")) });
        header.Inlines.Add(new Run("   " + m.DisplayTime) { FontSize = 10, Foreground = (Brush)FindResource("Muted") });
        stack.Children.Add(header);
        stack.Children.Add(BuildRichText(m));

        var menu = new ContextMenu();
        if (m.Platform is "TWITCH" or "YOUTUBE")
        {
            var timeout = new MenuItem { Header = "Таймаут 10 хв" };
            timeout.Click += async (_, _) => await ModerateSafe(m, false);
            var ban = new MenuItem { Header = "Заблокувати" };
            ban.Click += async (_, _) => await ModerateSafe(m, true);
            var del = new MenuItem { Header = "Видалити повідомлення" };
            del.Click += async (_, _) => { try { await _core.DeleteMessageAsync(m); } catch (Exception ex) { ShowError(ex); } };
            menu.Items.Add(timeout);
            menu.Items.Add(ban);
            menu.Items.Add(new Separator());
            menu.Items.Add(del);
            border.ContextMenu = menu;
        }

        ChatStack.Children.Add(border);
        while (ChatStack.Children.Count > 160) ChatStack.Children.RemoveAt(0);
        ChatCountText.Text = ChatStack.Children.Count.ToString();
        ChatScroll.ScrollToEnd();
    }

    private TextBlock BuildRichText(ChatMessage m)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = _core.Settings.Value.MainChatFontSize,
            Foreground = BrushFrom(_core.Settings.Value.MainChatTextColor, (Brush)FindResource("Text")),
            Margin = new Thickness(0, 2, 0, 0)
        };
        var text = m.Text ?? string.Empty;
        var emotes = (m.Emotes ?? new List<ChatEmote>())
            .Where(x => x.Start >= 0 && x.End < text.Length && x.End >= x.Start)
            .OrderBy(x => x.Start)
            .ToList();
        var pos = 0;
        foreach (var e in emotes)
        {
            if (e.Start < pos) continue;
            if (e.Start > pos) tb.Inlines.Add(new Run(text[pos..e.Start]));
            try
            {
                var image = new Image
                {
                    Width = 24,
                    Height = 24,
                    Stretch = Stretch.Uniform,
                    ToolTip = e.Name,
                    Margin = new Thickness(2, 0, 2, -5)
                };
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(e.ImageUrl, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                image.Source = bmp;
                tb.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
            }
            catch { tb.Inlines.Add(new Run(e.Name)); }
            pos = e.End + 1;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
        return tb;
    }

    private async Task ModerateSafe(ChatMessage m, bool ban)
    {
        try
        {
            await _core.ModerateAsync(m, ban);
            FooterStatus.Text = ban ? "Користувача заблоковано" : "Таймаут застосовано";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static Brush BrushFrom(string value, Brush fallback)
    {
        try { return new BrushConverter().ConvertFromString(value) as Brush ?? fallback; }
        catch { return fallback; }
    }

    private void RefreshStatus()
    {
        TwitchStatus.Text = CompactStatus(_core.Twitch.Status);
        YouTubeStatus.Text = CompactStatus(_core.YouTube.Status);
        StreamlabsStatus.Text = CompactStatus(_core.Streamlabs.Status);
        DonatelloStatus.Text = CompactStatus(_core.Donatello.Status);
        DiscordStatus.Text = CompactStatus(_core.NotifyBot.Status);
        TwitchStatus.Foreground = StatusBrush(_core.Twitch.Status);
        YouTubeStatus.Foreground = StatusBrush(_core.YouTube.Status);
        StreamlabsStatus.Foreground = StatusBrush(_core.Streamlabs.Status);
        DonatelloStatus.Foreground = StatusBrush(_core.Donatello.Status);
        DiscordStatus.Foreground = StatusBrush(_core.NotifyBot.Status);
        RefreshLiveTime();
    }

    private void RefreshLiveTime()
    {
        LiveTimeText.Text = LiteApp.LiveClock.DisplayText;
        LiveTimeText.Foreground = LiteApp.LiveClock.IsLive ? (Brush)FindResource("Green") : (Brush)FindResource("Muted");
    }

    private Brush StatusBrush(string status)
    {
        var s = status ?? string.Empty;
        if (s.Contains("ПІДКЛЮЧЕНО", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("ПРАЦЮЄ", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("LIVE", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("ЧАТ ПІДКЛЮЧЕНО", StringComparison.OrdinalIgnoreCase)) return (Brush)FindResource("Green");
        if (s.Contains("ПОМИЛ", StringComparison.OrdinalIgnoreCase) || s.Contains("КВОТ", StringComparison.OrdinalIgnoreCase)) return (Brush)FindResource("Red");
        return (Brush)FindResource("Muted");
    }

    private static string CompactStatus(string? status)
    {
        var s = (status ?? "—").Trim();
        return s switch
        {
            "ЧАТ ПІДКЛЮЧЕНО" => "ПІДКЛЮЧЕНО",
            "НЕ ПІДКЛЮЧЕНО" => "OFF",
            "ЕФІР НЕ ЗНАЙДЕНО" => "НЕМА ЕФІРУ",
            "ПОШУК ТРАНСЛЯЦІЇ" => "ПОШУК",
            _ => s
        };
    }

    private async Task Send(string target)
    {
        var text = ChatInput.Text.Trim();
        if (text.Length == 0) return;
        try
        {
            await _core.SendChatAsync(text, target);
            ChatInput.Clear();
            FooterStatus.Text = "Надіслано: " + target;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void SendTwitch_Click(object sender, RoutedEventArgs e) => await Send("TWITCH");
    private async void SendYouTube_Click(object sender, RoutedEventArgs e) => await Send("YOUTUBE");
    private async void SendBoth_Click(object sender, RoutedEventArgs e) => await Send("BOTH");
    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await Send("BOTH");
        }
    }

    private async void SkipAlert_Click(object sender, RoutedEventArgs e) { try { await _core.Streamlabs.SkipAlertAsync(); } catch (Exception ex) { ShowError(ex); } }
    private async void PauseAlerts_Click(object sender, RoutedEventArgs e) { try { await _core.Streamlabs.PauseAlertsAsync(); } catch (Exception ex) { ShowError(ex); } }
    private async void ResumeAlerts_Click(object sender, RoutedEventArgs e) { try { await _core.Streamlabs.ResumeAlertsAsync(); } catch (Exception ex) { ShowError(ex); } }

    private void Settings_Click(object sender, RoutedEventArgs e) { new SettingsWindow { Owner = this }.ShowDialog(); RefreshStatus(); }
    private void OverlaySettings_Click(object sender, RoutedEventArgs e) => new GameOverlaySettingsWindow { Owner = this }.ShowDialog();
    private void ChatBot_Click(object sender, RoutedEventArgs e) => new ChatBotWindow { Owner = this }.ShowDialog();
    private void Discord_Click(object sender, RoutedEventArgs e) => new DiscordBotWindow { Owner = this }.ShowDialog();

    private void Hud_Click(object sender, RoutedEventArgs e)
    {
        if (_hud is { IsVisible: true }) HideOverlay();
        else ShowOverlay();
    }

    public void ShowOverlay()
    {
        if (_hud is { IsVisible: true }) { _hud.Activate(); return; }
        _hud = new HudWindow(_core);
        _hud.Closed += (_, _) => _hud = null;
        _hud.Show();
    }

    public void HideOverlay()
    {
        try { _hud?.Close(); } catch { }
        _hud = null;
    }

    public void ApplyOverlaySettings() => _hud?.ApplySettings();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) Maximize_Click(sender, e);
        else try { DragMove(); } catch { }
    }
    private void ShowError(Exception ex) => FooterStatus.Text = ex.GetBaseException().Message;

    public void ApplyCiDemo()
    {
        _core.Stats.TwitchViewers = 15;
        _core.Stats.YouTubeViewers = 23;
        _core.Stats.YouTubeLikes = 16;
        AddChatRow(new ChatMessage { Platform = "TWITCH", User = "Cobra11", Text = "Kappa Україна разом!", Role = "Subscriber", Foreground = "#A970FF", Time = DateTime.Now });
        AddChatRow(new ChatMessage { Platform = "YOUTUBE", User = "PlayerUA", Text = "Добрий вечір 👋", Role = "Viewer", Foreground = "#43CDFF", Time = DateTime.Now });
        _core.Events.Add(new LiteEvent { Platform = "STREAMLABS • TWITCH", Type = "FOLLOW", User = "Cobra11", Text = "Новий фоловер", Accent = "#A970FF" });
        _core.Events.Add(new LiteEvent { Platform = "STREAMLABS • YOUTUBE", Type = "MEMBER", User = "PlayerUA", Text = "Став спонсором каналу", Accent = "#FF4B4B" });
        _core.Donations.Add(new DonationEvent { Source = "DONATELLO", User = "Sensei_Jzargo", Amount = 500, Currency = "UAH", Message = "Підтримка" });
        RefreshStatus();
    }
}
