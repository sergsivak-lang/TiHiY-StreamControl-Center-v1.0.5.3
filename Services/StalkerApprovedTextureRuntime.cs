using System.Runtime.CompilerServices;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerApprovedExactBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));

        EventManager.RegisterClassHandler(
            typeof(SettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSettingsWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            _ = StalkerApprovedExactRuntime.Attach(window);
    }

    private static void OnSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsWindow window)
            _ = StalkerApprovedExactSettingsRuntime.Attach(window);
    }
}

internal static class StalkerApprovedAssets
{
    internal const string Root = "pack://application:,,,/Assets/Themes/StalkerApproved/";

    internal static BitmapImage Load(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Root + file, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static Image NewImage(string file, Stretch stretch, double opacity = 1.0) => new()
    {
        Source = Load(file),
        Stretch = stretch,
        Opacity = opacity,
        IsHitTestVisible = false,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true
    };

    internal static ImageBrush NewTiledBrush(string file, double width, double height, double opacity)
    {
        var brush = new ImageBrush(Load(file))
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, width, height),
            Stretch = Stretch.Fill,
            Opacity = opacity
        };
        brush.Freeze();
        return brush;
    }

    internal static ImageBrush NewStretchBrush(string file, double opacity)
    {
        var brush = new ImageBrush(Load(file))
        {
            Stretch = Stretch.Fill,
            Opacity = opacity
        };
        brush.Freeze();
        return brush;
    }

    internal static bool IsStalkerTheme() => string.Equals(
        App.Services.Theme.CurrentTheme,
        "Сталкер",
        StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var visited = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (current is T match) yield return match;

            try
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                    pending.Push(VisualTreeHelper.GetChild(current, index));
            }
            catch { }

            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                    pending.Push(child);
            }
            catch { }
        }
    }

    internal static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is FrameworkElement element && element.Parent is not null) return element.Parent;
        if (current is FrameworkContentElement content && content.Parent is not null) return content.Parent;
        try { return VisualTreeHelper.GetParent(current); }
        catch { return null; }
    }
}

public static class StalkerApprovedExactRuntime
{
    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    public static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private static readonly IReadOnlyDictionary<string, string> PanelShells =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ChatBlockPanel"] = "chat-shell.png",
                ["DonationsBlockPanel"] = "donations-shell.png",
                ["MixerBlockPanel"] = "mixer-shell.png",
                ["NotificationsBlockPanel"] = "notifications-shell.png",
                ["SystemStatusBlockPanel"] = "system-status-shell.png",
                ["SystemMonitorPanel"] = "aida-shell.png"
            };

        private static readonly HashSet<string> ShellHeaderTexts = new(StringComparer.OrdinalIgnoreCase)
        {
            "МУЛЬТИЧАТ • TWITCH + YOUTUBE",
            "ДОНАТИ",
            "ШВИДКИЙ МІКШЕР • AUDIO MIXER OBS",
            "СПОВІЩЕННЯ",
            "СТАН СИСТЕМИ",
            "AIDA64 LIVE"
        };

        private readonly MainWindow _window;
        private readonly Dictionary<FrameworkElement, UIElement[]> _decorations = new();
        private readonly Dictionary<Border, BorderState> _borderStates = new();
        private readonly Dictionary<ContentControl, ContentState> _contentStates = new();
        private readonly Dictionary<Control, ControlState> _controlStates = new();
        private readonly Dictionary<TextBlock, TextState> _textStates = new();
        private readonly Dictionary<UIElement, double> _opacityStates = new();
        private readonly Dictionary<Border, MetricState> _metricStates = new();
        private bool _disposed;
        private bool _lastStalker;

        internal Controller(MainWindow window)
        {
            _window = window;
_window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Loaded);
        }

        private void ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);

        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();

            if (stalker)
            {
                ApplyRoot(true);
                ApplyExactHeaderAndOuterFrame(true);
                ApplyExactPanelShells(true);
                ApplyExactCenterPanel(true);
                ApplyAidaMetricLayout(true);
                ApplyLiveControls(true);
                ApplyTypography(true);
                ApplyArtworkVisibility(true);
            }
            else if (_lastStalker)
            {
                ClearStalkerOverrides();
            }

            _lastStalker = stalker;
        }

        private void ClearStalkerOverrides()
        {
            foreach (var parts in _decorations.Values)
                SetVisibility(parts, false);

            foreach (var border in _borderStates.Keys)
            {
                border.ClearValue(Border.BackgroundProperty);
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
                border.ClearValue(Border.CornerRadiusProperty);
            }

            foreach (var block in _contentStates.Keys)
            {
                block.ClearValue(Control.BackgroundProperty);
                block.ClearValue(Control.BorderBrushProperty);
                block.ClearValue(Control.BorderThicknessProperty);
                block.ClearValue(Control.PaddingProperty);
            }

            foreach (var control in _controlStates.Keys)
            {
                control.ClearValue(Control.BackgroundProperty);
                control.ClearValue(Control.BorderBrushProperty);
                control.ClearValue(Control.BorderThicknessProperty);
                control.ClearValue(Control.ForegroundProperty);
                control.ClearValue(Control.FontFamilyProperty);
            }

            foreach (var text in _textStates.Keys)
            {
                text.ClearValue(TextBlock.ForegroundProperty);
                text.ClearValue(TextBlock.FontFamilyProperty);
                text.ClearValue(TextBlock.FontWeightProperty);
            }

            foreach (var element in _opacityStates.Keys)
                element.ClearValue(UIElement.OpacityProperty);

            foreach (var border in _metricStates.Keys)
            {
                border.ClearValue(FrameworkElement.WidthProperty);
                border.ClearValue(FrameworkElement.HeightProperty);
                border.ClearValue(FrameworkElement.MarginProperty);
                border.ClearValue(Border.BackgroundProperty);
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
                border.ClearValue(Border.CornerRadiusProperty);
            }

            _borderStates.Clear();
            _contentStates.Clear();
            _controlStates.Clear();
            _textStates.Clear();
            _opacityStates.Clear();
            _metricStates.Clear();

            _window.InvalidateVisual();
            _window.InvalidateMeasure();
            _window.InvalidateArrange();
        }

        private void ApplyRoot(bool stalker)
        {
            if (_window.Content is not Border root) return;
            SaveBorder(root);
            if (stalker)
            {
                root.Background = StalkerApprovedAssets.NewTiledBrush("panel-fill-dark.png", 345, 50, 0.98);
                root.BorderBrush = Brushes.Transparent;
                root.BorderThickness = new Thickness(0);
                root.CornerRadius = new CornerRadius(0);
            }
            else RestoreBorder(root);
        }

        private void ApplyExactHeaderAndOuterFrame(bool stalker)
        {
            var host = _window.Content is Border { Child: Grid grid } ? grid : null;
            if (host is null) return;

            var parts = EnsureDecorations(host, () =>
            {
                var header = StalkerApprovedAssets.NewImage("header-full-exact.png", Stretch.Fill);
                header.Width = 1672;
                header.Height = 145;
                header.HorizontalAlignment = HorizontalAlignment.Left;
                header.VerticalAlignment = VerticalAlignment.Top;
                Panel.SetZIndex(header, -100);

                var top = Edge("outer-top.png", HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 18);
                var bottom = Edge("outer-bottom.png", HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 21);
                var left = Edge("outer-left.png", HorizontalAlignment.Left, VerticalAlignment.Stretch, 18, double.NaN);
                var right = Edge("outer-right.png", HorizontalAlignment.Right, VerticalAlignment.Stretch, 18, double.NaN);
                Panel.SetZIndex(top, 10000);
                Panel.SetZIndex(bottom, 10000);
                Panel.SetZIndex(left, 10000);
                Panel.SetZIndex(right, 10000);

                host.Children.Add(header);
                host.Children.Add(top);
                host.Children.Add(bottom);
                host.Children.Add(left);
                host.Children.Add(right);
                return new UIElement[] { header, top, bottom, left, right };
            });
            SetVisibility(parts, stalker);

            // The exact approved header already contains the emblem and title.
            foreach (var text in StalkerApprovedAssets.FindDescendants<TextBlock>(_window).Where(x =>
                         x.Text == "TiHiY" ||
                         x.Text?.Contains("StreamControl Center", StringComparison.OrdinalIgnoreCase) == true ||
                         x.Text?.Contains("MULTICHAT  •  DONATIONS", StringComparison.OrdinalIgnoreCase) == true))
                SetOpacity(text, stalker ? 0 : RestoreOpacity(text));
        }

        private static Image Edge(string file, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
        {
            var image = StalkerApprovedAssets.NewImage(file, Stretch.Fill);
            image.HorizontalAlignment = horizontal;
            image.VerticalAlignment = vertical;
            if (!double.IsNaN(width)) image.Width = width;
            if (!double.IsNaN(height)) image.Height = height;
            return image;
        }

        private void ApplyExactPanelShells(bool stalker)
        {
            foreach (var (name, file) in PanelShells)
            {
                var block = FindNamed<ContentControl>(name);
                if (block is null) continue;
                SaveContent(block);
                if (stalker)
                {
                    block.Background = StalkerApprovedAssets.NewStretchBrush(file, 1.0);
                    block.BorderBrush = Brushes.Transparent;
                    block.BorderThickness = new Thickness(0);
                    // Preserve the real row geometry while keeping live content inside
                    // the exact shell's frame and title bands.
                    block.Padding = name switch
                    {
                        "ChatBlockPanel" or "DonationsBlockPanel" => new Thickness(28, 12, 26, 13),
                        "MixerBlockPanel" or "NotificationsBlockPanel" => new Thickness(28, 10, 26, 10),
                        "SystemStatusBlockPanel" => new Thickness(25, 10, 24, 12),
                        "SystemMonitorPanel" => new Thickness(22, 9, 20, 10),
                        _ => block.Padding
                    };
                }
                else RestoreContent(block);
            }

            foreach (var text in StalkerApprovedAssets.FindDescendants<TextBlock>(_window))
            {
                if (text.Text is not null && ShellHeaderTexts.Contains(text.Text))
                    SetOpacity(text, stalker ? 0 : RestoreOpacity(text));
            }
        }

        private void ApplyExactCenterPanel(bool stalker)
        {
            var footer = FindNamed<Grid>("FooterBlocksGrid");
            var center = footer?.Children.OfType<ContentControl>().FirstOrDefault(x => Grid.GetColumn(x) == 2);
            if (center is null) return;
            SaveContent(center);
            if (stalker)
            {
                center.Background = StalkerApprovedAssets.NewStretchBrush("center-zone-panel-exact.png", 1.0);
                center.BorderBrush = Brushes.Transparent;
                center.BorderThickness = new Thickness(0);
                center.Padding = new Thickness(0);
            }
            else RestoreContent(center);

            foreach (var child in StalkerApprovedAssets.FindDescendants<UIElement>(center).Where(x => !ReferenceEquals(x, center)))
                SetOpacity(child, stalker ? 0 : RestoreOpacity(child));
        }

        private void ApplyAidaMetricLayout(bool stalker)
        {
            var monitor = FindNamed<ContentControl>("SystemMonitorPanel");
            if (monitor is null) return;

            foreach (var border in StalkerApprovedAssets.FindDescendants<Border>(monitor))
            {
                var labels = StalkerApprovedAssets.FindDescendants<TextBlock>(border)
                    .Select(x => x.Text ?? string.Empty).ToArray();
                if (!labels.Any(x => x is "CPU" or "GPU" or "RAM" or "FPS")) continue;

                if (!_metricStates.ContainsKey(border))
                    _metricStates[border] = new MetricState(
                        border.Width, border.Height, border.CornerRadius, border.Background,
                        border.BorderBrush, border.BorderThickness, border.Margin);

                if (stalker)
                {
                    border.Width = double.NaN;
                    border.Height = double.NaN;
                    border.CornerRadius = new CornerRadius(0);
                    border.Background = Brushes.Transparent;
                    border.BorderBrush = Brushes.Transparent;
                    border.BorderThickness = new Thickness(0);
                    border.Margin = new Thickness(5, 2, 5, 2);
                }
                else if (_metricStates.TryGetValue(border, out var state))
                {
                    border.Width = state.Width;
                    border.Height = state.Height;
                    border.CornerRadius = state.CornerRadius;
                    border.Background = state.Background;
                    border.BorderBrush = state.BorderBrush;
                    border.BorderThickness = state.BorderThickness;
                    border.Margin = state.Margin;
                }
            }

            foreach (var label in StalkerApprovedAssets.FindDescendants<TextBlock>(monitor).Where(x =>
                         x.Text is "CPU" or "GPU" or "RAM" or "FPS"))
                SetOpacity(label, stalker ? 0 : RestoreOpacity(label));
        }

        private void ApplyLiveControls(bool stalker)
        {
            foreach (var control in StalkerApprovedAssets.FindDescendants<Control>(_window))
            {
                if (control is not Button && control is not TextBox && control is not ComboBox &&
                    control is not PasswordBox && control is not ListBoxItem)
                    continue;

                if (!_controlStates.ContainsKey(control))
                    _controlStates[control] = new ControlState(
                        control.Background, control.BorderBrush, control.Foreground,
                        control.BorderThickness, control.FontFamily);

                if (stalker)
                {
                    control.Background = StalkerApprovedAssets.NewTiledBrush("panel-fill-dark.png", 345, 50, 0.90);
                    control.BorderBrush = new SolidColorBrush(Color.FromRgb(126, 86, 35));
                    control.BorderThickness = new Thickness(1);
                    control.Foreground = new SolidColorBrush(Color.FromRgb(226, 218, 194));
                    control.FontFamily = new FontFamily("Consolas");
                }
                else if (_controlStates.TryGetValue(control, out var state))
                {
                    control.Background = state.Background;
                    control.BorderBrush = state.BorderBrush;
                    control.Foreground = state.Foreground;
                    control.BorderThickness = state.BorderThickness;
                    control.FontFamily = state.FontFamily;
                }
            }
        }

        private void ApplyTypography(bool stalker)
        {
            foreach (var text in StalkerApprovedAssets.FindDescendants<TextBlock>(_window))
            {
                if (!_textStates.ContainsKey(text))
                    _textStates[text] = new TextState(text.Foreground, text.FontFamily, text.FontWeight);

                if (stalker)
                {
                    text.FontFamily = new FontFamily("Consolas");
                    var current = text.Foreground as SolidColorBrush;
                    if (current is not null && current.Color.G > current.Color.R + 25)
                        continue; // keep live green status values
                    if (text.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight() || text.FontSize >= 14)
                        text.Foreground = new SolidColorBrush(Color.FromRgb(202, 149, 73));
                    else
                        text.Foreground = new SolidColorBrush(Color.FromRgb(214, 208, 190));
                }
                else if (_textStates.TryGetValue(text, out var state))
                {
                    text.Foreground = state.Foreground;
                    text.FontFamily = state.FontFamily;
                    text.FontWeight = state.FontWeight;
                }
            }
        }

        private void ApplyArtworkVisibility(bool stalker)
        {
            foreach (var image in StalkerApprovedAssets.FindDescendants<Image>(_window))
            {
                var source = image.Source?.ToString() ?? string.Empty;
                if (source.Contains("StalkerApproved", StringComparison.OrdinalIgnoreCase)) continue;
                if (!source.Contains("UkraineExact", StringComparison.OrdinalIgnoreCase) &&
                    !source.Contains("StalkerExact", StringComparison.OrdinalIgnoreCase)) continue;
                SetOpacity(image, stalker ? 0 : RestoreOpacity(image));
            }
        }

        private UIElement[] EnsureDecorations(FrameworkElement owner, Func<UIElement[]> factory)
        {
            if (_decorations.TryGetValue(owner, out var existing)) return existing;
            var created = factory();
            _decorations[owner] = created;
            return created;
        }

        private static void SetVisibility(IEnumerable<UIElement> elements, bool visible)
        {
            foreach (var element in elements)
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            StalkerApprovedAssets.FindDescendants<T>(_window).FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.Ordinal));

        private void SaveBorder(Border border)
        {
            if (_borderStates.ContainsKey(border)) return;
            _borderStates[border] = new BorderState(
                border.Background, border.BorderBrush, border.BorderThickness, border.CornerRadius);
        }

        private void RestoreBorder(Border border)
        {
            if (!_borderStates.TryGetValue(border, out var state)) return;
            border.Background = state.Background;
            border.BorderBrush = state.BorderBrush;
            border.BorderThickness = state.BorderThickness;
            border.CornerRadius = state.CornerRadius;
        }

        private void SaveContent(ContentControl block)
        {
            if (_contentStates.ContainsKey(block)) return;
            _contentStates[block] = new ContentState(
                block.Background, block.BorderBrush, block.BorderThickness, block.Padding);
        }

        private void RestoreContent(ContentControl block)
        {
            if (!_contentStates.TryGetValue(block, out var state)) return;
            block.Background = state.Background;
            block.BorderBrush = state.BorderBrush;
            block.BorderThickness = state.BorderThickness;
            block.Padding = state.Padding;
        }

        private void SetOpacity(UIElement element, double opacity)
        {
            if (!_opacityStates.ContainsKey(element)) _opacityStates[element] = element.Opacity;
            element.Opacity = opacity;
        }

        private double RestoreOpacity(UIElement element) =>
            _opacityStates.TryGetValue(element, out var opacity) ? opacity : 1.0;

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record BorderState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness, CornerRadius CornerRadius);
        private sealed record ContentState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness, Thickness Padding);
        private sealed record ControlState(Brush? Background, Brush? BorderBrush, Brush? Foreground, Thickness BorderThickness, FontFamily FontFamily);
        private sealed record TextState(Brush? Foreground, FontFamily FontFamily, FontWeight FontWeight);
        private sealed record MetricState(double Width, double Height, CornerRadius CornerRadius, Brush? Background, Brush? BorderBrush, Thickness BorderThickness, Thickness Margin);
    }
}

public static class StalkerApprovedExactSettingsRuntime
{
    private static readonly ConditionalWeakTable<SettingsWindow, Controller> Controllers = new();

    public static IDisposable Attach(SettingsWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private readonly SettingsWindow _window;
        private readonly Dictionary<Border, BorderState> _borders = new();
        private readonly Dictionary<Control, ControlState> _controls = new();
        private readonly Dictionary<Image, ImageState> _images = new();
        private readonly Dictionary<TextBlock, TextState> _texts = new();
        private bool _disposed;
        private bool _lastStalker;

        internal Controller(SettingsWindow window)
        {
            _window = window;
_window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Loaded);
        }

        private void ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);

        private void ApplyNow()
        {
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                ApplyBorders(true);
                ApplyControls(true);
                ApplyImages(true);
                ApplyTexts(true);
            }
            else if (_lastStalker)
            {
                foreach (var border in _borders.Keys)
                {
                    border.ClearValue(Border.BackgroundProperty);
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BorderThicknessProperty);
                    border.ClearValue(Border.CornerRadiusProperty);
                }
                foreach (var control in _controls.Keys)
                {
                    control.ClearValue(Control.BackgroundProperty);
                    control.ClearValue(Control.BorderBrushProperty);
                    control.ClearValue(Control.BorderThicknessProperty);
                    control.ClearValue(Control.ForegroundProperty);
                    control.ClearValue(Control.FontFamilyProperty);
                }
                foreach (var image in _images.Keys)
                {
                    image.ClearValue(UIElement.VisibilityProperty);
                    image.ClearValue(UIElement.OpacityProperty);
                }
                foreach (var text in _texts.Keys)
                {
                    text.ClearValue(TextBlock.ForegroundProperty);
                    text.ClearValue(TextBlock.FontFamilyProperty);
                    text.ClearValue(TextBlock.FontWeightProperty);
                    text.ClearValue(UIElement.OpacityProperty);
                }
                _borders.Clear();
                _controls.Clear();
                _images.Clear();
                _texts.Clear();
                _window.InvalidateVisual();
            }
            _lastStalker = stalker;
        }

        private void ApplyBorders(bool stalker)
        {
            foreach (var border in StalkerApprovedAssets.FindDescendants<Border>(_window))
            {
                if (!_borders.ContainsKey(border))
                    _borders[border] = new BorderState(border.Background, border.BorderBrush, border.BorderThickness, border.CornerRadius);

                if (stalker)
                {
                    border.Background = StalkerApprovedAssets.NewTiledBrush("panel-fill-dark.png", 345, 50, 0.94);
                    border.BorderBrush = StalkerApprovedAssets.NewStretchBrush("panel-edge-top.png", 0.96);
                    if (border.BorderThickness.Left < 1) border.BorderThickness = new Thickness(1);
                    border.CornerRadius = new CornerRadius(0);
                }
                else if (_borders.TryGetValue(border, out var state))
                {
                    border.Background = state.Background;
                    border.BorderBrush = state.BorderBrush;
                    border.BorderThickness = state.BorderThickness;
                    border.CornerRadius = state.CornerRadius;
                }
            }
        }

        private void ApplyControls(bool stalker)
        {
            foreach (var control in StalkerApprovedAssets.FindDescendants<Control>(_window))
            {
                if (control is not Button && control is not TextBox && control is not ComboBox &&
                    control is not PasswordBox && control is not ListBoxItem)
                    continue;

                if (!_controls.ContainsKey(control))
                    _controls[control] = new ControlState(control.Background, control.BorderBrush, control.Foreground, control.BorderThickness, control.FontFamily);

                if (stalker)
                {
                    control.Background = StalkerApprovedAssets.NewTiledBrush("panel-fill.png", 345, 60, 0.92);
                    control.BorderBrush = StalkerApprovedAssets.NewStretchBrush("panel-edge-top.png", 0.98);
                    control.BorderThickness = new Thickness(1);
                    control.Foreground = new SolidColorBrush(Color.FromRgb(226, 218, 194));
                    control.FontFamily = new FontFamily("Consolas");
                }
                else if (_controls.TryGetValue(control, out var state))
                {
                    control.Background = state.Background;
                    control.BorderBrush = state.BorderBrush;
                    control.Foreground = state.Foreground;
                    control.BorderThickness = state.BorderThickness;
                    control.FontFamily = state.FontFamily;
                }
            }
        }

        private void ApplyImages(bool stalker)
        {
            foreach (var image in StalkerApprovedAssets.FindDescendants<Image>(_window))
            {
                var source = image.Source?.ToString() ?? string.Empty;
                if (!source.Contains("UkraineExact", StringComparison.OrdinalIgnoreCase)) continue;

                if (!_images.ContainsKey(image))
                    _images[image] = new ImageState(image.Visibility, image.Opacity);

                if (stalker)
                {
                    image.Visibility = Visibility.Collapsed;
                    image.Opacity = 0;
                }
                else if (_images.TryGetValue(image, out var state))
                {
                    image.Visibility = state.Visibility;
                    image.Opacity = state.Opacity;
                }
            }
        }

        private void ApplyTexts(bool stalker)
        {
            foreach (var text in StalkerApprovedAssets.FindDescendants<TextBlock>(_window))
            {
                if (!_texts.ContainsKey(text))
                    _texts[text] = new TextState(text.Foreground, text.FontFamily, text.FontWeight);

                if (stalker)
                {
                    text.FontFamily = new FontFamily("Consolas");
                    if (text.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight() || text.FontSize >= 13)
                        text.Foreground = new SolidColorBrush(Color.FromRgb(202, 149, 73));
                }
                else if (_texts.TryGetValue(text, out var state))
                {
                    text.Foreground = state.Foreground;
                    text.FontFamily = state.FontFamily;
                    text.FontWeight = state.FontWeight;
                }
            }
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record BorderState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness, CornerRadius CornerRadius);
        private sealed record ControlState(Brush? Background, Brush? BorderBrush, Brush? Foreground, Thickness BorderThickness, FontFamily FontFamily);
        private sealed record ImageState(Visibility Visibility, double Opacity);
        private sealed record TextState(Brush? Foreground, FontFamily FontFamily, FontWeight FontWeight);
    }
}



