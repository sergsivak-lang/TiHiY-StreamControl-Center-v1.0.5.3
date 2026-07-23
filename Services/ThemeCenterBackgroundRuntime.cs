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
/// Final owner of the lower-center footer artwork.
/// The artwork is a single persistent Image placed directly in FooterBlocksGrid.
/// It is not a descendant of the center ContentControl, so the STALKER shell cannot
/// hide it when it clears or changes descendant opacity.
/// </summary>
internal static class ThemeCenterBackgroundBootstrap
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
            _ = ThemeCenterBackgroundRuntime.Attach(window);
    }
}

internal static class ThemeCenterBackgroundRuntime
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
        private const string UkraineArtworkUri =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/UkraineExact/central-glory.jpg";

        private const string StalkerArtworkUri =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerApproved/center-zone-panel-exact.png";

        private readonly MainWindow _window;
        private readonly BitmapImage _ukraineImage;
        private readonly BitmapImage _stalkerImage;

        private Grid? _footer;
        private ContentControl? _centerPanel;
        private Image? _artworkLayer;
        private object? _originalContent;
        private Brush? _originalBackground;
        private Thickness _originalPadding;
        private bool _captured;
        private bool _applyQueued;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _ukraineImage = LoadFrozenImage(UkraineArtworkUri);
            _stalkerImage = LoadFrozenImage(StalkerArtworkUri);

            _window.ContentRendered += OnContentRendered;
            _window.Closed += OnWindowClosed;
            App.Services.Theme.ThemeChanged += OnThemeChanged;

            QueueApply();
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            _window.ContentRendered -= OnContentRendered;
            QueueApply();
        }

        private void OnThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply()
        {
            if (_disposed || _applyQueued) return;
            _applyQueued = true;

            // Run after all theme writers. No timers and no SizeChanged handlers.
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyQueued = false;
                ApplyCurrentTheme();
            }), DispatcherPriority.SystemIdle);
        }

        private void ApplyCurrentTheme()
        {
            if (_disposed || !_window.IsLoaded) return;
            if (!EnsureFooterLayer()) return;

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            _artworkLayer!.Source = stalker ? _stalkerImage : _ukraineImage;
            _artworkLayer.Visibility = Visibility.Visible;
            _artworkLayer.Opacity = 1.0;

            // Remove the old hard-coded Ukraine composition. The persistent footer
            // image is now the only owner of the inner artwork for both themes.
            _centerPanel!.Content = null;
            _centerPanel.Background = Brushes.Transparent;
            _centerPanel.Padding = new Thickness(0);

            _artworkLayer.InvalidateMeasure();
            _artworkLayer.InvalidateArrange();
            _artworkLayer.InvalidateVisual();
        }

        private bool EnsureFooterLayer()
        {
            if (_footer is not null && _centerPanel is not null && _artworkLayer is not null)
                return true;

            _footer = StalkerApprovedAssets.FindDescendants<Grid>(_window)
                .FirstOrDefault(x => string.Equals(
                    x.Name,
                    "FooterBlocksGrid",
                    StringComparison.Ordinal));

            _centerPanel = _footer?.Children
                .OfType<ContentControl>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 2);

            if (_footer is null || _centerPanel is null) return false;

            if (!_captured)
            {
                _captured = true;
                _originalContent = _centerPanel.Content;
                _originalBackground = _centerPanel.Background;
                _originalPadding = _centerPanel.Padding;
            }

            if (_artworkLayer is null)
            {
                _artworkLayer = new Image
                {
                    Name = "ThemeCenterArtworkLayer",
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(8, 7, 8, 7),
                    ClipToBounds = true,
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true,
                    Visibility = Visibility.Visible,
                    Opacity = 1.0
                };

                Grid.SetColumn(_artworkLayer, 2);
                Grid.SetRow(_artworkLayer, 0);
                Panel.SetZIndex(_artworkLayer, 5000);
                _footer.Children.Add(_artworkLayer);
            }

            return true;
        }

        private static BitmapImage LoadFrozenImage(string uri)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _window.ContentRendered -= OnContentRendered;
            _window.Closed -= OnWindowClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;

            if (_footer is not null && _artworkLayer is not null)
                _footer.Children.Remove(_artworkLayer);

            if (_captured && _centerPanel is not null)
            {
                _centerPanel.Content = _originalContent;
                _centerPanel.Background = _originalBackground;
                _centerPanel.Padding = _originalPadding;
            }
        }
    }
}
