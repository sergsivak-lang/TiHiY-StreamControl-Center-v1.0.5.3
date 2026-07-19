using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Owns the few visual replacements that must be fully reversible when switching
/// between STALKER and Ukraine. It deliberately snapshots real live content and
/// restores it instead of clearing dependency properties blindly.
/// </summary>
internal static class StalkerThemeTransitionGuardBootstrap
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
            _ = StalkerThemeTransitionGuardRuntime.Attach(window);
    }
}

internal static class StalkerThemeTransitionGuardRuntime
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
        private ContentControl? _center;
        private object? _centerOriginalContent;
        private Brush? _centerOriginalBackground;
        private Brush? _centerOriginalBorderBrush;
        private Thickness _centerOriginalBorderThickness;
        private Thickness _centerOriginalPadding;
        private bool _centerCaptured;

        private FrameworkElement? _mixerContent;
        private Thickness _mixerOriginalMargin;
        private bool _mixerCaptured;

        private readonly Dictionary<Image, HeaderImageState> _headerImages = new();
        private bool _lastStalker;
        private bool _disposed;
        private bool _applyQueued;

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
        private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (StalkerApprovedAssets.IsStalkerTheme()) QueueApply();
        }
        private void ThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply()
        {
            if (_disposed || _applyQueued) return;
            _applyQueued = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyQueued = false;
                Apply();
            }), DispatcherPriority.ContextIdle);
        }

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();

            if (stalker)
            {
                ApplyCenterArtwork();
                LowerMixerContent();
                StretchApprovedHeader();
            }
            else if (_lastStalker)
            {
                RestoreCenter();
                RestoreMixerContent();
                RestoreHeaderImages();
            }

            _lastStalker = stalker;
        }

        private void ApplyCenterArtwork()
        {
            var footer = FindNamed<Grid>("FooterBlocksGrid");
            _center ??= FindNamed<ContentControl>("ModulesBlockPanel")
                       ?? footer?.Children.OfType<ContentControl>()
                           .FirstOrDefault(item => Grid.GetColumn(item) == 2);
            if (_center is null) return;

            if (!_centerCaptured)
            {
                _centerOriginalContent = _center.Content;
                _centerOriginalBackground = _center.Background;
                _centerOriginalBorderBrush = _center.BorderBrush;
                _centerOriginalBorderThickness = _center.BorderThickness;
                _centerOriginalPadding = _center.Padding;
                _centerCaptured = true;
            }

            if (_center.Content is not Image image || image.Tag as string != "StalkerTransitionCenter")
            {
                image = StalkerApprovedAssets.NewImage("center-zone-panel-exact.png", Stretch.Fill);
                image.Tag = "StalkerTransitionCenter";
                image.HorizontalAlignment = HorizontalAlignment.Stretch;
                image.VerticalAlignment = VerticalAlignment.Stretch;
                _center.Content = image;
            }

            _center.Background = Brushes.Transparent;
            _center.BorderBrush = Brushes.Transparent;
            _center.BorderThickness = new Thickness(0);
            _center.Padding = new Thickness(0);
            _center.Visibility = Visibility.Visible;
            _center.Opacity = 1;
        }

        private void RestoreCenter()
        {
            if (!_centerCaptured || _center is null) return;
            _center.Content = _centerOriginalContent;
            _center.Background = _centerOriginalBackground;
            _center.BorderBrush = _centerOriginalBorderBrush;
            _center.BorderThickness = _centerOriginalBorderThickness;
            _center.Padding = _centerOriginalPadding;
        }

        private void LowerMixerContent()
        {
            var mixer = FindNamed<ContentControl>("MixerBlockPanel");
            if (mixer?.Content is not FrameworkElement content) return;

            if (!ReferenceEquals(_mixerContent, content))
            {
                _mixerContent = content;
                _mixerOriginalMargin = content.Margin;
                _mixerCaptured = true;
            }

            // Keep the outer STALKER shell fixed; only lower the live channel rows.
            content.Margin = new Thickness(
                _mixerOriginalMargin.Left,
                _mixerOriginalMargin.Top + 12,
                _mixerOriginalMargin.Right,
                Math.Max(0, _mixerOriginalMargin.Bottom - 4));
        }

        private void RestoreMixerContent()
        {
            if (_mixerCaptured && _mixerContent is not null)
                _mixerContent.Margin = _mixerOriginalMargin;
        }

        private void StretchApprovedHeader()
        {
            foreach (var image in StalkerApprovedAssets.FindDescendants<Image>(_window))
            {
                var source = image.Source?.ToString() ?? string.Empty;
                if (!source.Contains("header-full-exact.png", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!_headerImages.ContainsKey(image))
                    _headerImages[image] = new HeaderImageState(image.Width, image.HorizontalAlignment);

                image.Width = double.NaN;
                image.HorizontalAlignment = HorizontalAlignment.Stretch;
                image.Margin = new Thickness(0);
            }
        }

        private void RestoreHeaderImages()
        {
            foreach (var (image, state) in _headerImages)
            {
                image.Width = state.Width;
                image.HorizontalAlignment = state.HorizontalAlignment;
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
            _window.Closed -= WindowClosed;
            _window.ContentRendered -= ContentRendered;
            _window.SizeChanged -= WindowSizeChanged;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record HeaderImageState(double Width, HorizontalAlignment HorizontalAlignment);
    }
}
