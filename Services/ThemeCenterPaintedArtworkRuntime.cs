using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Permanently replaces the old lower-center ContentControl with one plain painted
/// Border. The border is not a Control and contains no Image child, so the STALKER
/// control/Ukraine-image cleanup runtimes cannot blank it. Its background is switched
/// directly between the Ukraine and STALKER artwork resources.
/// </summary>
internal static class ThemeCenterPaintedArtworkBootstrap
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
        if (sender is MainWindow window)
            _ = ThemeCenterPaintedArtworkRuntime.Attach(window);
    }
}

internal static class ThemeCenterPaintedArtworkRuntime
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
        private const string UkraineResource =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/UkraineExact/central-glory.jpg";

        private const string StalkerResource =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerApproved/center-zone-panel-exact.png";

        private readonly MainWindow _window;
        private readonly ImageBrush _ukraineBrush;
        private readonly ImageBrush _stalkerBrush;

        private Grid? _footer;
        private ContentControl? _removedCenter;
        private int _removedIndex = -1;
        private Border? _paintedCenter;
        private bool _applyQueued;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _ukraineBrush = CreateBrush(UkraineResource);
            _stalkerBrush = CreateBrush(StalkerResource);

            _window.ContentRendered += OnContentRendered;
            _window.Closed += OnWindowClosed;
            App.Services.Theme.ThemeChanged += OnThemeChanged;

            QueueApply(DispatcherPriority.Loaded);
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            _window.ContentRendered -= OnContentRendered;
            QueueApply(DispatcherPriority.SystemIdle);
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            QueueApply(DispatcherPriority.SystemIdle);
        }

        private void QueueApply(DispatcherPriority priority)
        {
            if (_disposed || _applyQueued)
                return;

            _applyQueued = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyQueued = false;
                EnsureAndApply();
            }), priority);
        }

        private void EnsureAndApply()
        {
            if (_disposed || !_window.IsLoaded)
                return;

            if (!EnsurePaintedCenter())
                return;

            // Only the two approved themes use this decorative footer artwork.
            // The application currently opens in one of these themes during testing;
            // any non-STALKER value deliberately receives the Ukraine artwork instead
            // of leaving the column empty because of a localized/string mismatch.
            _paintedCenter!.Background = StalkerApprovedAssets.IsStalkerTheme()
                ? _stalkerBrush
                : _ukraineBrush;

            _paintedCenter.Visibility = Visibility.Visible;
            _paintedCenter.Opacity = 1.0;
            _paintedCenter.IsEnabled = true;
            _paintedCenter.InvalidateMeasure();
            _paintedCenter.InvalidateArrange();
            _paintedCenter.InvalidateVisual();
        }

        private bool EnsurePaintedCenter()
        {
            if (_paintedCenter is not null)
                return true;

            _footer = StalkerApprovedAssets.FindDescendants<Grid>(_window)
                .FirstOrDefault(x => string.Equals(
                    x.Name,
                    "FooterBlocksGrid",
                    StringComparison.Ordinal));

            if (_footer is null)
                return false;

            _removedCenter = _footer.Children
                .OfType<ContentControl>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 2);

            if (_removedCenter is not null)
            {
                _removedIndex = _footer.Children.IndexOf(_removedCenter);
                _footer.Children.Remove(_removedCenter);
            }

            _paintedCenter = new Border
            {
                Name = "ThemePaintedCenterPanel",
                Margin = new Thickness(3, 0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Background = Brushes.Transparent,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Visible,
                Opacity = 1.0,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            Grid.SetColumn(_paintedCenter, 2);
            Panel.SetZIndex(_paintedCenter, int.MaxValue);

            if (_removedIndex >= 0 && _removedIndex <= _footer.Children.Count)
                _footer.Children.Insert(_removedIndex, _paintedCenter);
            else
                _footer.Children.Add(_paintedCenter);

            return true;
        }

        private static ImageBrush CreateBrush(string resourceUri)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(resourceUri, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            var brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None,
                Opacity = 1.0
            };
            brush.Freeze();
            return brush;
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _window.ContentRendered -= OnContentRendered;
            _window.Closed -= OnWindowClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;

            if (_footer is not null && _paintedCenter is not null)
                _footer.Children.Remove(_paintedCenter);

            if (_footer is not null && _removedCenter is not null)
            {
                if (_removedIndex >= 0 && _removedIndex <= _footer.Children.Count)
                    _footer.Children.Insert(_removedIndex, _removedCenter);
                else
                    _footer.Children.Add(_removedCenter);
            }
        }
    }
}
