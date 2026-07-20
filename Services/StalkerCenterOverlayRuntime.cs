using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Keeps the real footer center artwork deterministic. The center panel in the
/// Ukraine XAML has no name and older theme runtimes mutate its content, so this
/// overlay is attached directly to FooterBlocksGrid column 2 and always wins the
/// final visual layer without touching the live footer layout.
/// </summary>
internal static class StalkerCenterOverlayBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            _ = StalkerCenterOverlayRuntime.Attach(window);
    }
}

internal static class StalkerCenterOverlayRuntime
{
    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    internal static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private readonly MainWindow _window;
        private Grid? _footer;
        private Image? _overlay;
        private DispatcherTimer? _settleTimer;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _window.Closed += WindowClosed;
            _window.ContentRendered += ContentRendered;
            _window.SizeChanged += WindowSizeChanged;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            QueueApply();
        }

        private void ContentRendered(object? sender, EventArgs e) => QueueApply();
        private void WindowSizeChanged(object sender, SizeChangedEventArgs e) => QueueApply();

        private void ThemeChanged(object? sender, EventArgs e)
        {
            QueueApply();
            _settleTimer?.Stop();
            _settleTimer = new DispatcherTimer(DispatcherPriority.ContextIdle, _window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _settleTimer.Tick += SettleTimerTick;
            _settleTimer.Start();
        }

        private void SettleTimerTick(object? sender, EventArgs e)
        {
            _settleTimer?.Stop();
            if (_settleTimer is not null) _settleTimer.Tick -= SettleTimerTick;
            _settleTimer = null;
            Apply();
        }

        private void QueueApply()
        {
            if (_disposed) return;
            _window.Dispatcher.BeginInvoke(new Action(Apply), DispatcherPriority.ContextIdle);
        }

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;
            _footer ??= FindNamed<Grid>("FooterBlocksGrid");
            if (_footer is null) return;

            if (_overlay is null || !ReferenceEquals(_overlay.Parent, _footer))
            {
                _overlay = StalkerApprovedAssets.NewImage("center-zone-banner.png", Stretch.UniformToFill);
                _overlay.Tag = "StalkerCenterOverlay";
                _overlay.HorizontalAlignment = HorizontalAlignment.Stretch;
                _overlay.VerticalAlignment = VerticalAlignment.Stretch;
                _overlay.Margin = new Thickness(3, 0);
                _overlay.IsHitTestVisible = false;
                Grid.SetColumn(_overlay, 2);
                Panel.SetZIndex(_overlay, 50000);
                _footer.Children.Add(_overlay);
            }

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            _overlay.Visibility = stalker ? Visibility.Visible : Visibility.Collapsed;
            _overlay.Opacity = stalker ? 1 : 0;

            if (stalker)
            {
                Grid.SetColumn(_overlay, 2);
                Panel.SetZIndex(_overlay, 50000);
            }
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            StalkerApprovedAssets.FindDescendants<T>(_window)
                .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settleTimer?.Stop();
            if (_settleTimer is not null) _settleTimer.Tick -= SettleTimerTick;
            _window.Closed -= WindowClosed;
            _window.ContentRendered -= ContentRendered;
            _window.SizeChanged -= WindowSizeChanged;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }
    }
}
