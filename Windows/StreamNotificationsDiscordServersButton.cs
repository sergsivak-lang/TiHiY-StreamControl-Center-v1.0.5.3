using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TiHiY.StreamControlCenter.Windows;

internal static class StreamNotificationsDiscordServersButton
{
    private const string Marker = "TIHIY_DISCORD_SERVERS_BUTTON";

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(StreamNotificationsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not StreamNotificationsWindow window) return;
        window.Dispatcher.BeginInvoke(new Action(() => EnsureButton(window)));
    }

    private static void EnsureButton(StreamNotificationsWindow window)
    {
        if (FindDescendant<Button>(window, b => string.Equals(b.Tag?.ToString(), Marker, StringComparison.Ordinal)) is not null)
            return;

        var openChannels = FindDescendant<Button>(window, b =>
            string.Equals(b.Content?.ToString(), "ВІДКРИТИ КАНАЛИ", StringComparison.OrdinalIgnoreCase));

        if (openChannels?.Parent is not WrapPanel panel) return;

        var button = new Button
        {
            Content = "СЕРВЕРИ DISCORD",
            Tag = Marker,
            ToolTip = "Показати сервери Discord, канали та права бота; вибрати канали для стрімів і донатів.",
            Background = new SolidColorBrush(Color.FromRgb(41, 53, 119))
        };

        button.Click += (_, _) =>
            App.Services.Windows.Show(() => new DiscordServersWindow(), window);

        var index = panel.Children.IndexOf(openChannels);
        panel.Children.Insert(Math.Max(0, index), button);
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) return match;
            var nested = FindDescendant(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }
}
