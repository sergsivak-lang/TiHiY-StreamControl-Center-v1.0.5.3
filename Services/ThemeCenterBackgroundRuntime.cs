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
/// Uses the ContentControl background instead of a child overlay, so the STALKER
/// shell cannot hide it by changing descendant opacity. Applies after all other
/// theme runtimes both at startup and on every theme change.
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
        // Assembly-qualified pack URIs are required for resources embedded in this
        // executable. The previous unqualified URIs failed silently, leaving the
        // center empty at STALKER startup and the hard-coded Ukraine child visible
        // after switching themes.
        private const string UkraineArtworkUri =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/UkraineExact/central-glory.jpg";

        private const string StalkerArtworkUri =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerApproved/center-zone-panel-exact.png";

        private readonly MainWindow _window;
        private readonly ImageBrush _ukraineBrush;
        private readonly ImageBrush _stalkerBrush;

        private ContentControl? _centerPanel;
        private object? _originalContent;
        private Brush? _originalBackground;
        private Thickness _originalPadding;
        private HorizontalAlignment _originalHorizontalContentAlignment;
        private VerticalAlignment _originalVerticalContentAlignment;
        private bool _originalClipToBounds;
        private Visibility _originalVisibility;
        private double _originalOpacity;
        private bool _captured;
        private bool _applyQueued;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _ukraineBrush = LoadFrozenBrush(UkraineArtworkUri);
            _stalkerBrush = LoadFrozenBrush(StalkerArtworkUri);

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

            // SystemIdle is deliberately later than Render, Loaded, ContextIdle and
            // ApplicationIdle. This makes this class the final writer without timers.
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyQueued = false;
                ApplyCurrentTheme();
            }), DispatcherPriority.SystemIdle);
        }

        private void ApplyCurrentTheme()
        {
            if (_disposed || !_window.IsLoaded) return;
            if (!EnsureCenterPanel()) return;

            var brush = StalkerApprovedAssets.IsStalkerTheme()
                ? _stalkerBrush
                : _ukraineBrush;

            // Remove the old hard-coded Ukraine child. Both themes now use exactly one
            // background artwork surface, so stale images cannot survive a theme switch.
            _centerPanel!.Content = null;
            _centerPanel.Background = brush;
            _centerPanel.Padding = new Thickness(0);
            _centerPanel.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _centerPanel.VerticalContentAlignment = VerticalAlignment.Stretch;
            _centerPanel.ClipToBounds = true;
            _centerPanel.Visibility = Visibility.Visible;
            _centerPanel.Opacity = 1.0;

            _centerPanel.InvalidateMeasure();
            _centerPanel.InvalidateArrange();
            _centerPanel.InvalidateVisual();
        }

        private bool EnsureCenterPanel()
        {
            if (_centerPanel is not null) return true;

            var footer = StalkerApprovedAssets.FindDescendants<Grid>(_window)
                .FirstOrDefault(x => string.Equals(
                    x.Name,
                    "FooterBlocksGrid",
                    StringComparison.Ordinal));

            _centerPanel = footer?.Children
                .OfType<ContentControl>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 2);

            if (_centerPanel is null) return false;

            if (!_captured)
            {
                _captured = true;
                _originalContent = _centerPanel.Content;
                _originalBackground = _centerPanel.Background;
                _originalPadding = _centerPanel.Padding;
                _originalHorizontalContentAlignment = _centerPanel.HorizontalContentAlignment;
                _originalVerticalContentAlignment = _centerPanel.VerticalContentAlignment;
                _originalClipToBounds = _centerPanel.ClipToBounds;
                _originalVisibility = _centerPanel.Visibility;
                _originalOpacity = _centerPanel.Opacity;
            }

            return true;
        }

        private static ImageBrush LoadFrozenBrush(string uri)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();

            var brush = new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };
            brush.Freeze();
            return brush;
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _window.ContentRendered -= OnContentRendered;
            _window.Closed -= OnWindowClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;

            if (_captured && _centerPanel is not null)
            {
                _centerPanel.Content = _originalContent;
                _centerPanel.Background = _originalBackground;
                _centerPanel.Padding = _originalPadding;
                _centerPanel.HorizontalContentAlignment = _originalHorizontalContentAlignment;
                _centerPanel.VerticalContentAlignment = _originalVerticalContentAlignment;
                _centerPanel.ClipToBounds = _originalClipToBounds;
                _centerPanel.Visibility = _originalVisibility;
                _centerPanel.Opacity = _originalOpacity;
            }
        }
    }
}
