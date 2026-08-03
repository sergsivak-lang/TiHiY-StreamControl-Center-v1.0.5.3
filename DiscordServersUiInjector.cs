using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter;

internal static class DiscordServersUiInjector
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(StreamNotificationsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnNotificationsLoaded));
    }

    private static void OnNotificationsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not StreamNotificationsWindow window) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => InjectButton(window)));
    }

    private static void InjectButton(StreamNotificationsWindow window)
    {
        if (FindNamedButton(window, "СЕРВЕРИ DISCORD") is not null) return;
        var anchor = FindNamedButton(window, "ВІДКРИТИ КАНАЛИ");
        if (anchor?.Parent is not Panel panel) return;

        var button = new Button
        {
            Content = "СЕРВЕРИ DISCORD",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            MinWidth = 132,
            ToolTip = "Показати всі сервери Discord, де встановлений бот, перевірити права та вибрати канали для стрімів і донатів."
        };
        if (window.TryFindResource(typeof(Button)) is Style style)
            button.Style = style;
        button.Click += (_, _) => App.Services.Windows.Show(() => new DiscordServersWindow(), window);

        var index = panel.Children.IndexOf(anchor);
        panel.Children.Insert(Math.Min(index + 1, panel.Children.Count), button);
    }

    private static Button? FindNamedButton(DependencyObject root, string content)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
                return button;
            if (FindNamedButton(child, content) is { } nested) return nested;
        }
        return null;
    }
}
