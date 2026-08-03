using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter;

internal static class MiniPerformanceMode
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
        if (sender is not MainWindow main) return;
        main.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => Apply(main)));
    }

    private static void Apply(MainWindow main)
    {
        try
        {
            StopDispatcherTimer(main, "_audioRefreshTimer");
            StopDispatcherTimer(main, "_systemRefreshTimer");
            DisposeMainWindowVisualTuner();

            var settings = App.Services.Settings.Value;
            settings.AutoConnectObs = false;
            settings.Aida64MonitoringEnabled = false;

            App.Services.Logger.Info("MINI performance: OBS audio refresh, PC/AIDA64 refresh and legacy dashboard visual tuner disabled.");
        }
        catch (Exception ex)
        {
            try { App.Services.Logger.Error("MINI performance mode", ex); } catch { }
        }
    }

    private static void StopDispatcherTimer(object owner, string fieldName)
    {
        var field = owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(owner) is DispatcherTimer timer)
            timer.Stop();
    }

    private static void DisposeMainWindowVisualTuner()
    {
        if (Application.Current is not App app) return;
        var field = typeof(App).GetField("_mainWindowVisualTuner", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(app) is IDisposable disposable)
        {
            disposable.Dispose();
            field.SetValue(app, null);
        }
    }
}
