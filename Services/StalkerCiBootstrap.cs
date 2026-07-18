namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerCiBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        var arguments = Environment.GetCommandLineArgs();
        var isScreenshotRun = arguments.Any(x =>
            x.StartsWith("--ci-screenshot=", StringComparison.OrdinalIgnoreCase));
        var explicitlyUkraine = arguments.Any(x =>
            string.Equals(x, "--ci-apply-ukraine-theme", StringComparison.OrdinalIgnoreCase));

        if (isScreenshotRun && !explicitlyUkraine && App.Services is not null)
            App.Services.Theme.Apply("Сталкер", save: false);
    }
}
