using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerSettingsCiRuntime
{
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
        var arguments = Environment.GetCommandLineArgs();
        if (!arguments.Any(x => string.Equals(x, "--ci-apply-stalker-theme", StringComparison.OrdinalIgnoreCase)))
            return;

        App.Services.Theme.Apply("Сталкер", save: false);
        window.Dispatcher.BeginInvoke(new Action(() => SelectStalkerTheme(window)), DispatcherPriority.Loaded);
        window.Dispatcher.BeginInvoke(new Action(() => SelectStalkerTheme(window)), DispatcherPriority.Render);
    }

    private static void SelectStalkerTheme(SettingsWindow window)
    {
        if (window.FindName("ThemeList") is not ListBox list) return;
        var item = list.Items.Cast<object>().FirstOrDefault(candidate =>
        {
            var property = candidate.GetType().GetProperty("Name");
            return string.Equals(property?.GetValue(candidate)?.ToString(), "Сталкер", StringComparison.OrdinalIgnoreCase);
        });
        if (item is null) return;
        list.SelectedItem = item;
        list.ScrollIntoView(item);
    }
}
