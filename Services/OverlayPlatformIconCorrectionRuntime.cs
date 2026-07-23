using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Ensures every live overlay uses the real platform assets instead of a
/// fallback application glyph or a stale theme image.
/// </summary>
internal static class OverlayPlatformIconCorrectionBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(LocalChatOverlayWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnOverlayLoaded));
    }

    private static void OnOverlayLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not LocalChatOverlayWindow window)
            return;

        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            SetIcon(window, "TwitchOverlayIcon", "twitch.png", 18, 18);
            SetIcon(window, "YouTubeOverlayIcon", "youtube.png", 22, 18);
        }));
    }

    private static void SetIcon(FrameworkElement root, string name, string file, double width, double height)
    {
        if (root.FindName(name) is not Image image)
            return;

        var source = new BitmapImage();
        source.BeginInit();
        source.UriSource = new Uri(
            $"pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Platforms/{file}",
            UriKind.Absolute);
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.EndInit();
        source.Freeze();

        image.Source = source;
        image.Width = width;
        image.Height = height;
        image.Stretch = Stretch.Uniform;
        image.SnapsToDevicePixels = true;
        image.UseLayoutRounding = true;
    }
}
