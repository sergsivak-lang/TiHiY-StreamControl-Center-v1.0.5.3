namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerThemeFinalizerBootstrap
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
            _ = StalkerThemeFinalizer.Attach(window);
    }
}

public static class StalkerThemeFinalizer
{
    private sealed class Controller : IDisposable
    {
        private readonly MainWindow _window;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<Border, BorderState> _borderStates = new();
        private readonly Dictionary<TextBlock, TextState> _textStates = new();
        private bool _disposed;
        private bool _lastWasStalker;

        public Controller(MainWindow window)
        {
            _window = window;
            _timer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(90)
            };
            _timer.Tick += Timer_Tick;
            _window.LayoutUpdated += Window_LayoutUpdated;
            _window.Closed += Window_Closed;
            App.Services.Theme.ThemeChanged += Theme_ThemeChanged;
            _timer.Start();
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);
        }

        private bool IsStalker => string.Equals(
            App.Services.Theme.CurrentTheme,
            "Сталкер",
            StringComparison.OrdinalIgnoreCase);

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || !_window.IsLoaded) return;
            if (IsStalker || _lastWasStalker)
                ApplyNow();
        }

        private void Window_LayoutUpdated(object? sender, EventArgs e)
        {
            if (_disposed || !IsStalker) return;
            ApplyCenterBanner(true);
        }

        private void Theme_ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);

        public void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = IsStalker;
            ApplyCenterBanner(stalker);
            ApplyInnerSurfaces(stalker);
            ApplySecondaryText(stalker);
            _lastWasStalker = stalker;
        }

        private void ApplyCenterBanner(bool stalker)
        {
            var source = stalker
                ? "/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerExact/zone-banner.png"
                : "/TiHiY.StreamControlCenter;component/Assets/Themes/UkraineExact/central-glory.png";

            var targetImage = FindDescendants<Image>(_window).FirstOrDefault(image =>
            {
                var current = image.Source?.ToString() ?? string.Empty;
                return current.Contains("central-glory", StringComparison.OrdinalIgnoreCase)
                       || current.Contains("zone-banner", StringComparison.OrdinalIgnoreCase);
            });

            if (targetImage is null)
            {
                targetImage = FindDescendants<ContentControl>(_window)
                    .Where(control => string.Equals(control.Tag?.ToString(), "ExactCenterTexture", StringComparison.Ordinal))
                    .Select(control => control.Content as Image)
                    .FirstOrDefault(image => image is not null);
            }

            if (targetImage is null) return;
            var currentSource = targetImage.Source?.ToString() ?? string.Empty;
            if (!currentSource.Contains(stalker ? "zone-banner" : "central-glory", StringComparison.OrdinalIgnoreCase))
                targetImage.Source = new BitmapImage(new Uri(source, UriKind.Relative));
            targetImage.Stretch = Stretch.UniformToFill;
            targetImage.SnapsToDevicePixels = true;

            if (FindAncestor<ContentControl>(targetImage) is { } host)
            {
                host.Tag = "ExactCenterTexture";
                host.Padding = new Thickness(0);
            }
        }

        private void ApplyInnerSurfaces(bool stalker)
        {
            foreach (var border in FindDescendants<Border>(_window))
            {
                if (ReferenceEquals(border, _window.Content)) continue;
                if (ContainsPlatformLogo(border)) continue;
                if (!ShouldTheme(border)) continue;

                if (!_borderStates.ContainsKey(border))
                    _borderStates[border] = new BorderState(
                        border.Background,
                        border.BorderBrush,
                        border.BorderThickness,
                        border.CornerRadius);

                if (stalker)
                {
                    var isMetric = ContainsAnyText(border, "CPU", "GPU", "RAM", "FPS", "OBS —");
                    var isDanger = ContainsAnyText(border, "НЕ ПІДКЛЮЧЕНО", "quota", "помилка");
                    border.Background = new SolidColorBrush(isMetric
                        ? Color.FromArgb(226, 9, 12, 8)
                        : Color.FromArgb(218, 12, 14, 9));
                    border.BorderBrush = new SolidColorBrush(isDanger
                        ? Color.FromRgb(126, 55, 35)
                        : isMetric
                            ? Color.FromRgb(166, 116, 35)
                            : Color.FromRgb(88, 76, 48));
                    if (border.BorderThickness == default || border.BorderThickness.Left < 0.8)
                        border.BorderThickness = new Thickness(1);
                    if (border.CornerRadius.TopLeft > 3)
                        border.CornerRadius = new CornerRadius(2);
                }
                else if (_borderStates.TryGetValue(border, out var original))
                {
                    border.Background = original.Background;
                    border.BorderBrush = original.BorderBrush;
                    border.BorderThickness = original.BorderThickness;
                    border.CornerRadius = original.CornerRadius;
                }
            }
        }

        private void ApplySecondaryText(bool stalker)
        {
            foreach (var text in FindDescendants<TextBlock>(_window))
            {
                if (!ShouldThemeText(text)) continue;
                if (!_textStates.ContainsKey(text))
                    _textStates[text] = new TextState(text.Foreground, text.FontFamily);

                if (stalker)
                {
                    text.Foreground = IsStatusOrValue(text)
                        ? new SolidColorBrush(Color.FromRgb(117, 199, 67))
                        : new SolidColorBrush(Color.FromRgb(163, 157, 137));
                    if (text.FontSize <= 13)
                        text.FontFamily = new FontFamily("Consolas");
                }
                else if (_textStates.TryGetValue(text, out var original))
                {
                    text.Foreground = original.Foreground;
                    text.FontFamily = original.FontFamily;
                }
            }
        }

        private static bool ShouldTheme(Border border)
        {
            if (border.ActualWidth < 28 || border.ActualHeight < 18) return false;
            var background = (border.Background as SolidColorBrush)?.Color;
            var outline = (border.BorderBrush as SolidColorBrush)?.Color;
            return IsBlueSurface(background) || IsBlueSurface(outline)
                   || (border.ActualWidth > 180 && border.ActualHeight > 32 && border.ActualHeight < 180);
        }

        private static bool ShouldThemeText(TextBlock text)
        {
            if (text.Text is null || text.Text.Length == 0) return false;
            if (text.Text.Contains("TWITCH", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase)) return false;
            var color = (text.Foreground as SolidColorBrush)?.Color;
            if (color is null) return false;
            return color.Value.B > color.Value.R + 20 && color.Value.B >= color.Value.G;
        }

        private static bool IsStatusOrValue(TextBlock text) =>
            text.Text.Contains("ПІДКЛЮЧЕНО", StringComparison.OrdinalIgnoreCase)
            || text.Text.Contains("OBS", StringComparison.OrdinalIgnoreCase)
            || text.Text.Contains("°", StringComparison.OrdinalIgnoreCase)
            || text.Text.Contains("%", StringComparison.OrdinalIgnoreCase);

        private static bool IsBlueSurface(Color? color) =>
            color is { } value && value.B > value.R + 18 && value.B > value.G + 4;

        private static bool ContainsPlatformLogo(DependencyObject root) =>
            FindDescendants<Image>(root).Any(image =>
                (image.Source?.ToString() ?? string.Empty).Contains("/Assets/Platforms/", StringComparison.OrdinalIgnoreCase));

        private static bool ContainsAnyText(DependencyObject root, params string[] fragments) =>
            FindDescendants<TextBlock>(root).Any(text =>
                fragments.Any(fragment => text.Text?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true));

        private void Window_Closed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _window.LayoutUpdated -= Window_LayoutUpdated;
            _window.Closed -= Window_Closed;
            App.Services.Theme.ThemeChanged -= Theme_ThemeChanged;
        }

        private sealed record BorderState(
            Brush Background,
            Brush BorderBrush,
            Thickness BorderThickness,
            CornerRadius CornerRadius);

        private sealed record TextState(Brush Foreground, FontFamily FontFamily);
    }

    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    public static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    public static void ApplyNow(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var controller)) controller.ApplyNow();
        else _ = Attach(window);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var visited = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            if (current is T match) yield return match;
            try
            {
                var count = VisualTreeHelper.GetChildrenCount(current);
                for (var index = 0; index < count; index++)
                    stack.Push(VisualTreeHelper.GetChild(current, index));
            }
            catch { }
            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                    stack.Push(child);
            }
            catch { }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = ParentOf(current))
            if (current is T match) return match;
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject current)
    {
        if (current is FrameworkElement element && element.Parent is not null) return element.Parent;
        if (current is FrameworkContentElement content && content.Parent is not null) return content.Parent;
        try { return VisualTreeHelper.GetParent(current); }
        catch { return null; }
    }
}
