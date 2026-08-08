using System.Runtime.InteropServices;
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
    private readonly LitePreferences _prefs;

    public HudWindow(LiteCoreService core)
    {
        InitializeComponent();
        _core = core;
        _prefs = core.Preferences.Value;
        Left = _prefs.HudLeft; Top = _prefs.HudTop; Width = _prefs.HudWidth; Height = _prefs.HudHeight;
        Opacity = Math.Clamp(_prefs.HudOpacity, 0.2, 1.0);
        HudRoot.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(_prefs.HudBackgroundOpacity,0,1)*255),3,15,27));
        _core.ChatAdded += Core_ChatAdded;
        _core.EventAdded += Core_EventAdded;
        _core.Stats.PropertyChanged += (_,_) => Dispatcher.BeginInvoke(new Action(UpdateStats));
        Loaded += (_,_) =>
        {
            UpdateStats();
            foreach (var m in _core.Chat.TakeLast(Math.Clamp(_prefs.HudMaxMessages,1,30))) AddChat(m);
        };
        SourceInitialized += (_,_) => ApplyWindowFlags();
        Closed += (_,_) =>
        {
            _core.ChatAdded -= Core_ChatAdded;
            _core.EventAdded -= Core_EventAdded;
            _prefs.HudLeft = Left; _prefs.HudTop = Top; _prefs.HudWidth = Width; _prefs.HudHeight = Height; _core.Preferences.Save();
        };
    }

    private void Core_ChatAdded(object? sender, ChatMessage e) => Dispatcher.BeginInvoke(new Action(() => AddChat(e)));
    private void Core_EventAdded(object? sender, LiteEvent e) => Dispatcher.BeginInvoke(new Action(() => ShowEvent(e)));

    private void AddChat(ChatMessage m)
    {
        HudChatStack.Children.Add(LiteChatVisual.BuildHudRow(m, _prefs.HudFontSize));
        while (HudChatStack.Children.Count > Math.Clamp(_prefs.HudMaxMessages,1,30)) HudChatStack.Children.RemoveAt(0);
        HudScroll.ScrollToEnd();
    }

    private void ShowEvent(LiteEvent e)
    {
        if (!_prefs.HudShowEvents) return;
        EventBar.Visibility = Visibility.Visible;
        EventText.Text = $"{e.Platform} • {e.Type} • {e.User}" + (string.IsNullOrWhiteSpace(e.Text) ? string.Empty : $" — {e.Text}");
    }

    private void UpdateStats()
    {
        var visible = _prefs.HudShowStats ? Visibility.Visible : Visibility.Collapsed;
        TwText.Visibility = visible; YtText.Visibility = visible; TotalText.Visibility = visible; LikesText.Visibility = visible;
        TwText.Text = _core.Stats.TwitchViewers.ToString();
        YtText.Text = _core.Stats.YouTubeViewers.ToString();
        TotalText.Text = _core.Stats.TotalViewers.ToString();
        LikesText.Text = _core.Stats.YouTubeLikes.ToString();
    }

    private void ApplyWindowFlags()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (_prefs.HudClickThrough) style |= WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
        try { SetWindowDisplayAffinity(hwnd, _prefs.HudExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); } catch { }
    }

    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
