using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

internal static class ThemeCenterArtworkBootstrap
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
            _ = ThemeCenterArtworkRuntime.Attach(window);
    }
}

internal static class ThemeCenterArtworkRuntime
{
    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    internal static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing))
            return existing;

        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private readonly MainWindow _window;
        private ContentControl? _center;
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
            _settleTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _settleTimer.Tick += SettleTimerTick;
            _settleTimer.Start();
        }

        private void SettleTimerTick(object? sender, EventArgs e)
        {
            _settleTimer?.Stop();
            if (_settleTimer is not null)
                _settleTimer.Tick -= SettleTimerTick;
            _settleTimer = null;
            Apply();
        }

        private void QueueApply()
        {
            if (_disposed) return;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Apply));
        }

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;

            _center = FindCenterPanel() ?? _center;
            if (_center is null) return;

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            var image = new Image
            {
                Source = stalker
                    ? StalkerApprovedAssets.Load("center-zone-panel-exact.png")
                    : LoadUkraineArtwork(),
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                IsHitTestVisible = false,
                Tag = "ThemeCenterArtwork"
            };

            _center.Content = image;
            _center.Background = Brushes.Black;
            _center.BorderBrush = Brushes.Transparent;
            _center.BorderThickness = new Thickness(0);
            _center.Padding = new Thickness(0);
            _center.ClipToBounds = true;
            _center.Visibility = Visibility.Visible;
            _center.Opacity = 1;
        }

        private ContentControl? FindCenterPanel()
        {
            var footer = StalkerApprovedAssets.FindDescendants<Grid>(_window)
                .FirstOrDefault(item => string.Equals(item.Name, "FooterBlocksGrid", StringComparison.Ordinal));
            if (footer is null) return null;

            return footer.Children.OfType<ContentControl>()
                .FirstOrDefault(item => Grid.GetColumn(item) == 2);
        }

        private static BitmapImage LoadUkraineArtwork()
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(
                "pack://application:,,,/Assets/Themes/UkraineExact/central-glory.jpg",
                UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settleTimer?.Stop();
            if (_settleTimer is not null)
                _settleTimer.Tick -= SettleTimerTick;
            _window.Closed -= WindowClosed;
            _window.ContentRendered -= ContentRendered;
            _window.SizeChanged -= WindowSizeChanged;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }
    }
}
