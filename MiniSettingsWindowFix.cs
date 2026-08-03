using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter;

internal static class MiniSettingsWindowFix
{
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsSysMenu = 0x00080000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(SettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSettingsLoaded));
    }

    private static void OnSettingsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsWindow window) return;

        Apply(window);
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => Apply(window)));

        EventHandler? rendered = null;
        rendered = (_, _) =>
        {
            Apply(window);
            if (rendered is not null) window.ContentRendered -= rendered;
        };
        window.ContentRendered += rendered;
    }

    private static void Apply(SettingsWindow window)
    {
        window.Title = "TiHiY StreamControl MINI — Налаштування";
        try { window.WindowStyle = WindowStyle.None; } catch { }
        window.ResizeMode = ResizeMode.CanResize;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        RemoveNativeCaption(window);

        if (window.FindName("DesignSurface") is Grid design)
        {
            design.Margin = new Thickness(7);
            if (design.RowDefinitions.Count >= 3)
            {
                design.RowDefinitions[0].Height = new GridLength(96);
                design.RowDefinitions[2].Height = new GridLength(78);
            }
        }

        ReplaceLegacyBranding(window);
        HideUnusedTabs(window);
        FixFooter(window);
    }

    private static void ReplaceLegacyBranding(SettingsWindow window)
    {
        foreach (var text in FindDescendants<TextBlock>(window))
        {
            if (string.Equals(text.Text, "  StreamControl Center", StringComparison.Ordinal))
                text.Text = "  StreamControl MINI";
            else if (string.Equals(text.Text, "TiHiY StreamControl Center", StringComparison.Ordinal))
                text.Text = "TiHiY StreamControl MINI";
            else if (text.Text.Contains("Єдиний центр керування мультичатом, донатами, OBS Audio, музикою та сповіщеннями.", StringComparison.Ordinal))
                text.Text = "Компактний центр мультичату, донатів, Discord-сповіщень, віджетів та AIMP Now Playing.";
        }
    }

    private static void HideUnusedTabs(SettingsWindow window)
    {
        if (window.FindName("SettingsTabs") is not TabControl tabs) return;
        foreach (var tab in tabs.Items.OfType<TabItem>())
        {
            if (tab.Header is not DependencyObject header) continue;
            var label = string.Join(" ", FindDescendants<TextBlock>(header).Select(x => x.Text)).Trim();
            if (label.Contains("Музика", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Music", StringComparison.OrdinalIgnoreCase) ||
                label.StartsWith("OBS ", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("WebSocket", StringComparison.OrdinalIgnoreCase))
                tab.Visibility = Visibility.Collapsed;
        }
    }

    private static void FixFooter(SettingsWindow window)
    {
        if (window.FindName("StatusText") is not TextBlock status) return;
        var footer = FindAncestor<Border>(status);
        if (footer is null) return;

        footer.Margin = new Thickness(0, 6, 0, 0);
        footer.Padding = new Thickness(9, 8, 9, 8);
        footer.MinHeight = 66;
        footer.ClipToBounds = false;
        footer.VerticalAlignment = VerticalAlignment.Stretch;

        foreach (var button in FindDescendants<Button>(footer))
        {
            button.Height = 40;
            button.MinHeight = 40;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.Margin = new Thickness(4, 0, 0, 0);

            var label = GetButtonText(button);
            if (label.Contains("Скасувати", StringComparison.OrdinalIgnoreCase)) button.Width = 158;
            else if (label.Contains("Зберегти", StringComparison.OrdinalIgnoreCase)) button.Width = 158;
            else if (label.Contains("Застосувати", StringComparison.OrdinalIgnoreCase)) button.Width = 194;
        }
    }

    private static void RemoveNativeCaption(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var style = GetStyle(hwnd).ToInt64();
            var desired = style & ~WsCaption & ~WsSysMenu;
            if (desired == style) return;
            SetStyle(hwnd, new IntPtr(desired));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch { }
    }

    private static string GetButtonText(Button button)
    {
        if (button.Content is string text) return text;
        if (button.Content is DependencyObject root)
            return string.Join(" ", FindDescendants<TextBlock>(root).Select(x => x.Text));
        return string.Empty;
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T found) return found;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { current = null; }
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
        for (var i = 0; i < count; i++)
        {
            DependencyObject child;
            try { child = VisualTreeHelper.GetChild(root, i); }
            catch { continue; }
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static IntPtr GetStyle(IntPtr hwnd) => IntPtr.Size == 8
        ? GetWindowLongPtr64(hwnd, GwlStyle)
        : new IntPtr(GetWindowLong32(hwnd, GwlStyle));

    private static void SetStyle(IntPtr hwnd, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GwlStyle, value);
        else SetWindowLong32(hwnd, GwlStyle, value.ToInt32());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
