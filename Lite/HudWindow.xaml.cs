using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Windows.Interop;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

public partial class HudWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_NONE = 0x0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    private readonly LiteCoreService _core;
    private IntPtr _hwnd;
    private bool _clickThrough;

    public HudWindow(LiteCoreService core)
    {
        InitializeComponent();
        _core = core;
        RestorePlacement();
        ApplySettings();
        _core.ChatAdded += Core_ChatAdded;
        _core.Stats.PropertyChanged += Stats_PropertyChanged;
        _core.StatusChanged += Core_StatusChanged;
        Loaded += Window_Loaded;
        SourceInitialized += Window_SourceInitialized;
        Closed += Window_Closed;
    }

    public void ApplySettings()
    {
        var s = _core.Settings.Value;
        Topmost = true;
        _clickThrough = s.LocalChatOverlayClickThrough;
        ControlBar.Visibility = _clickThrough ? Visibility.Collapsed : Visibility.Visible;
        ModeText.Text = _clickThrough ? "КРІЗЬ КЛІКИ" : "КЕРУВАННЯ";
        OverlayFrame.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(s.LocalChatOverlayBackgroundOpacity, 0, .85) * 255), 0, 0, 0));
        ViewerStatsBar.SetValue(TextElement.FontSizeProperty, Math.Clamp(s.LocalChatOverlayFontSize * .65, 9, 20));
        ApplyWindowFlags();
        TrimMessages();
        RefreshStats();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var m in _core.Chat.TakeLast(Math.Max(3, _core.Settings.Value.LocalChatOverlayMaxMessages))) AddChat(m);
        RefreshStats();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyWindowFlags();
    }

    private void Core_ChatAdded(object? sender, ChatMessage e) => Dispatcher.BeginInvoke(new Action(() => AddChat(e)));
    private void Stats_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshStats));
    private void Core_StatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshStats));

    private void AddChat(ChatMessage m)
    {
        var row = LiteChatVisual.BuildHudRow(m, Math.Clamp(_core.Settings.Value.LocalChatOverlayFontSize, 11, 42));
        if (row is Border border) border.Background = Brushes.Transparent;
        OverlayChatStack.Children.Add(row);
        TrimMessages();
        OverlayScroll.ScrollToEnd();
    }

    private void TrimMessages()
    {
        var max = Math.Max(3, _core.Settings.Value.LocalChatOverlayMaxMessages);
        while (OverlayChatStack.Children.Count > max) OverlayChatStack.Children.RemoveAt(0);
    }

    private void RefreshStats()
    {
        var s = _core.Settings.Value;
        TwitchViewers.Text = s.TwitchViewers.ToString("N0");
        YouTubeViewers.Text = s.YouTubeViewers.ToString("N0");
        YouTubeLikes.Text = s.YouTubeLikes.ToString("N0");
        TwitchStatus.Text = s.TwitchLive ? "LIVE" : "OFF";
        YouTubeStatus.Text = s.YouTubeLive ? "LIVE" : "OFF";
        TwitchStatus.Foreground = s.TwitchLive ? Brushes.LimeGreen : Brushes.Gray;
        YouTubeStatus.Foreground = s.YouTubeLive ? Brushes.LimeGreen : Brushes.Gray;
        TwitchLiveDot.Fill = s.TwitchLive ? Brushes.LimeGreen : Brushes.Gray;
        YouTubeLiveDot.Fill = s.YouTubeLive ? Brushes.LimeGreen : Brushes.Gray;
    }

    private void ApplyWindowFlags()
    {
        if (_hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW;
        if (_clickThrough) style |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        else style &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(style));
        try { SetWindowDisplayAffinity(_hwnd, _core.Preferences.Value.HudExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); } catch { }
    }

    private void RestorePlacement()
    {
        var s = _core.Settings.Value;
        if (s.WindowPlacements.TryGetValue("LocalChatOverlay", out var w) && w.Width > 100 && w.Height > 100)
        {
            Left = w.Left; Top = w.Top; Width = w.Width; Height = w.Height;
            return;
        }
        var p = _core.Preferences.Value;
        Left = p.HudLeft; Top = p.HudTop; Width = p.HudWidth; Height = p.HudHeight;
    }

    private void SavePlacement()
    {
        var placement = new WindowPlacement { Left = Left, Top = Top, Width = ActualWidth, Height = ActualHeight };
        _core.Settings.Value.WindowPlacements["LocalChatOverlay"] = placement;
        var p = _core.Preferences.Value;
        p.HudLeft = Left; p.HudTop = Top; p.HudWidth = ActualWidth; p.HudHeight = ActualHeight;
        _core.SettingsService.Save(_core.Settings.Value);
        _core.Preferences.Save();
    }

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough || e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _core.ChatAdded -= Core_ChatAdded;
        _core.Stats.PropertyChanged -= Stats_PropertyChanged;
        _core.StatusChanged -= Core_StatusChanged;
        SavePlacement();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
