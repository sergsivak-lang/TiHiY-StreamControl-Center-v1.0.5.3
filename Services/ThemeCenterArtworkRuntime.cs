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
/// Owns only the inner artwork of the real lower-center footer block.
/// Ukraine and STALKER use separate committed resources. The same Image instance
/// is updated on theme changes, so there are no overlays, timers or SizeChanged loops.
/// </summary>
internal static class ThemeCenterArtworkBootstrap
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
            _ = ThemeCenterArtworkRuntime.Attach(window);
    }
}

internal static class ThemeCenterArtworkRuntime
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
        private readonly ContentControl? _centerPanel;
        private readonly Image? _artwork;
        private readonly object? _originalContent;
        private readonly Thickness _originalPadding;
        private readonly HorizontalAlignment _originalHorizontalContentAlignment;
        private readonly VerticalAlignment _originalVerticalContentAlignment;
        private readonly bool _originalClipToBounds;
        private readonly BitmapSource _ukraineArtwork;
        private readonly BitmapSource _stalkerArtwork;

        private bool _applyQueued;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _centerPanel = FindCenterPanel(window);

            _ukraineArtwork = LoadFrozenImage(UkraineArtworkUri);
            _stalkerArtwork = LoadFrozenImage(StalkerArtworkUri);

            if (_centerPanel is not null)
            {
                _originalContent = _centerPanel.Content;
                _originalPadding = _centerPanel.Padding;
                _originalHorizontalContentAlignment = _centerPanel.HorizontalContentAlignment;
                _originalVerticalContentAlignment = _centerPanel.VerticalContentAlignment;
                _originalClipToBounds = _centerPanel.ClipToBounds;

                _artwork = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    StretchDirection = StretchDirection.Both,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true,
                    IsHitTestVisible = false
                };

                _centerPanel.Content = _artwork;
                _centerPanel.Padding = new Thickness(0);
                _centerPanel.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                _centerPanel.VerticalContentAlignment = VerticalAlignment.Stretch;
                _centerPanel.ClipToBounds = true;
            }

            _window.Closed += OnWindowClosed;
            App.Services.Theme.ThemeChanged += OnThemeChanged;
            QueueApply();
        }

        private void OnThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply()
        {
            if (_disposed || _applyQueued) return;
            _applyQueued = true;

            // The existing theme shell runs at Render priority. ApplicationIdle runs
            // immediately afterwards and makes this dedicated artwork owner the final writer.
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyQueued = false;
                ApplyCurrentTheme();
            }), DispatcherPriority.ApplicationIdle);
        }

        private void ApplyCurrentTheme()
        {
            if (_disposed || _centerPanel is null || _artwork is null || !_window.IsLoaded)
                return;

            var isStalker = string.Equals(
                App.Services.Theme.CurrentTheme,
                "Сталкер",
                StringComparison.OrdinalIgnoreCase);

            _artwork.Source = isStalker ? _stalkerArtwork : _ukraineArtwork;
            _artwork.Opacity = 1.0;
            _artwork.Visibility = Visibility.Visible;

            // Reassert only the content geometry. Do not touch the outer panel shell,
            // border, margin or grid columns belonging to the active theme.
            _centerPanel.Content = _artwork;
            _centerPanel.Padding = new Thickness(0);
            _centerPanel.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _centerPanel.VerticalContentAlignment = VerticalAlignment.Stretch;
            _centerPanel.ClipToBounds = true;

            _centerPanel.InvalidateMeasure();
            _centerPanel.InvalidateArrange();
            _centerPanel.InvalidateVisual();
        }

        private static ContentControl? FindCenterPanel(MainWindow window)
        {
            var footer = StalkerApprovedAssets.FindDescendants<Grid>(window)
                .FirstOrDefault(x => string.Equals(
                    x.Name,
                    "FooterBlocksGrid",
                    StringComparison.Ordinal));

            return footer?.Children
                .OfType<ContentControl>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 2);
        }

        private static BitmapSource LoadFrozenImage(string uri)
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

            _window.Closed -= OnWindowClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;

            if (_centerPanel is not null)
            {
                _centerPanel.Content = _originalContent;
                _centerPanel.Padding = _originalPadding;
                _centerPanel.HorizontalContentAlignment = _originalHorizontalContentAlignment;
                _centerPanel.VerticalContentAlignment = _originalVerticalContentAlignment;
                _centerPanel.ClipToBounds = _originalClipToBounds;
            }
        }
    }
}
