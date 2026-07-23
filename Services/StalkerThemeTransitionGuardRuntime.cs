using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerThemeTransitionGuardBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
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
        private readonly Dictionary<FrameworkElement, Thickness> _contentMargins = new();
        private readonly Dictionary<Image, ImageState> _paintedHeaders = new();
        private ContentControl? _center;
        private object? _centerOriginalContent;
        private Brush? _centerOriginalBackground;
        private Brush? _centerOriginalBorderBrush;
        private Thickness _centerOriginalBorderThickness;
        private Thickness _centerOriginalPadding;
        private bool _centerCaptured;
        private Grid? _safeHeader;
        private bool _lastStalker;
        private bool _disposed;
        private bool _applyQueued;
        private DispatcherTimer? _settleTimer;

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

        private void ThemeChanged(object? sender, EventArgs e)
        {
            QueueApply();
            QueueSettleApply();
        }

        private void QueueApply()
        {
            if (_disposed || _applyQueued) return;
            _applyQueued = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyQueued = false;
                Apply();
            }), DispatcherPriority.ApplicationIdle);
        }

        // Several legacy theme runtimes also react to ThemeChanged. Reapply once after
        // they have completed so the final visible state is deterministic.
        private void QueueSettleApply()
        {
            _settleTimer?.Stop();
            _settleTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(180)
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

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                ApplyCenterArtwork();
                LowerPanelContents();
                ReplacePaintedHeader();
            }
            else if (_lastStalker)
            {
                RestoreCenter();
                RestorePanelContents();
                RestoreHeader();
            }
            _lastStalker = stalker;
        }

        private void ApplyCenterArtwork()
        {
            _center = FindRealCenterPanel() ?? _center;
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

            var image = _center.Content as Image;
            if (image?.Tag as string != "StalkerTransitionCenter")
            {
                image = StalkerApprovedAssets.NewImage("center-zone-banner.png", Stretch.UniformToFill);
                image.Tag = "StalkerTransitionCenter";
                _center.Content = image;
            }

            image.Opacity = 1;
            image.Visibility = Visibility.Visible;
            image.HorizontalAlignment = HorizontalAlignment.Stretch;
            image.VerticalAlignment = VerticalAlignment.Stretch;
            Panel.SetZIndex(image, 100);

            _center.Background = StalkerApprovedAssets.NewStretchBrush("center-zone-panel-exact.png", 1.0);
            _center.BorderBrush = Brushes.Transparent;
            _center.BorderThickness = new Thickness(0);
            _center.Padding = new Thickness(0);
            _center.Visibility = Visibility.Visible;
            _center.Opacity = 1;
        }

        private ContentControl? FindRealCenterPanel()
        {
            // Most reliable marker: the Ukraine center block contains this unique text.
            var marker = StalkerApprovedAssets.FindDescendants<TextBlock>(_window)
                .FirstOrDefault(x => string.Equals(x.Text, "СЛАВА УКРАЇНІ!", StringComparison.OrdinalIgnoreCase));
            for (DependencyObject? current = marker; current is not null; current = StalkerApprovedAssets.GetParent(current))
            {
                if (current is ContentControl content && content.IsVisible)
                    return content;
            }

            var footer = FindNamed<Grid>("FooterBlocksGrid");
            if (footer is null) return null;

            return footer.Children.OfType<ContentControl>()
                .Where(x => x.IsVisible && x.Name != "ModulesBlockPanel")
                .OrderBy(x => Math.Abs(Grid.GetColumn(x) - 2))
                .FirstOrDefault(x => Grid.GetColumn(x) == 2)
                ?? footer.Children.OfType<ContentControl>()
                    .FirstOrDefault(x => x.IsVisible && x.Name != "SystemStatusBlockPanel" && x.Name != "SystemMonitorPanel");
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

        private void LowerPanelContents()
        {
            ApplyContentOffset("ChatBlockPanel", 10);
            ApplyContentOffset("DonationsBlockPanel", 10);
            ApplyContentOffset("MixerBlockPanel", 14);
            ApplyContentOffset("NotificationsBlockPanel", 10);
            ApplyContentOffset("SystemStatusBlockPanel", 8);
            ApplyContentOffset("SystemMonitorPanel", 10);
        }

        private void ApplyContentOffset(string name, double offset)
        {
            var panel = FindNamed<ContentControl>(name);
            if (panel?.Content is not FrameworkElement content) return;
            if (!_contentMargins.ContainsKey(content)) _contentMargins[content] = content.Margin;
            var original = _contentMargins[content];
            content.Margin = new Thickness(original.Left, original.Top + offset,
                original.Right, Math.Max(0, original.Bottom - Math.Min(offset, 6)));
        }

        private void RestorePanelContents()
        {
            foreach (var (content, margin) in _contentMargins)
                content.Margin = margin;
        }

        private void ReplacePaintedHeader()
        {
            foreach (var image in StalkerApprovedAssets.FindDescendants<Image>(_window))
            {
                var source = image.Source?.ToString() ?? string.Empty;
                if (!source.Contains("header-full-exact.png", StringComparison.OrdinalIgnoreCase)) continue;
                if (!_paintedHeaders.ContainsKey(image))
                    _paintedHeaders[image] = new ImageState(image.Opacity, image.Visibility);
                image.Opacity = 0;
                image.Visibility = Visibility.Hidden;
            }

            if (_window.Content is not Border { Child: Grid host }) return;
            if (_safeHeader is null)
            {
                _safeHeader = new Grid
                {
                    Height = 145,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false
                };
                var skyline = StalkerApprovedAssets.NewImage("header-skyline-exact.png", Stretch.Fill, .9);
                skyline.HorizontalAlignment = HorizontalAlignment.Stretch;
                skyline.VerticalAlignment = VerticalAlignment.Stretch;
                _safeHeader.Children.Add(skyline);

                var title = StalkerApprovedAssets.NewImage("header-title-exact.png", Stretch.Uniform);
                title.Width = 700;
                title.Height = 95;
                title.HorizontalAlignment = HorizontalAlignment.Left;
                title.VerticalAlignment = VerticalAlignment.Top;
                _safeHeader.Children.Add(title);

                Panel.SetZIndex(_safeHeader, -99);
                host.Children.Add(_safeHeader);
            }
            _safeHeader.Visibility = Visibility.Visible;
        }

        private void RestoreHeader()
        {
            if (_safeHeader is not null) _safeHeader.Visibility = Visibility.Collapsed;
            foreach (var (image, state) in _paintedHeaders)
            {
                image.Opacity = state.Opacity;
                image.Visibility = state.Visibility;
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

        private sealed record ImageState(double Opacity, Visibility Visibility);
    }
}