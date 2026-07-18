using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerSettingsRuntimeBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(SettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsWindow window)
            _ = StalkerSettingsRuntime.Attach(window);
    }
}

public static class StalkerSettingsRuntime
{
    private sealed class Controller : IDisposable
    {
        private readonly SettingsWindow _window;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<Border, BorderState> _borderStates = new();
        private readonly Dictionary<Control, ControlState> _controlStates = new();
        private readonly Dictionary<TextBlock, TextState> _textStates = new();
        private readonly Dictionary<Image, ImageState> _imageStates = new();
        private bool _disposed;
        private bool _lastStalker;
        private Image? _headerBackdrop;
        private Image? _headerSymbol;

        public Controller(SettingsWindow window)
        {
            _window = window;
            _timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _timer.Tick += Timer_Tick;
            _window.Closed += Window_Closed;
            App.Services.Theme.ThemeChanged += Theme_ThemeChanged;
            _timer.Start();
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Loaded);
        }

        private bool IsStalker => string.Equals(
            App.Services.Theme.CurrentTheme,
            "Сталкер",
            StringComparison.OrdinalIgnoreCase);

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || !_window.IsLoaded) return;
            if (IsStalker || _lastStalker)
                ApplyNow();
        }

        private void Theme_ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);

        public void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = IsStalker;
            ApplyWindow(stalker);
            ApplyUkraineArtworkVisibility(stalker);
            ApplyHeaderArtwork(stalker);
            ApplyBorders(stalker);
            ApplyControls(stalker);
            ApplyText(stalker);
            _lastStalker = stalker;
        }

        private void ApplyWindow(bool stalker)
        {
            _window.Background = ResourceBrush(stalker ? "StalkerWindowBrush" : "UkraineWindowBrush", Brushes.Black);
            if (_window.Content is Border root)
            {
                root.Background = ResourceBrush(stalker ? "StalkerWindowBrush" : "UkraineWindowBrush", Brushes.Black);
                root.BorderBrush = stalker ? Brush("#9A571F") : Brush("#168ED8");
            }
            _window.Title = stalker
                ? "TiHiY StreamControl Center — STALKER Settings"
                : "TiHiY StreamControl Center — Налаштування";
        }

        private void ApplyUkraineArtworkVisibility(bool stalker)
        {
            foreach (var image in FindDescendants<Image>(_window))
            {
                var source = image.Source?.ToString() ?? string.Empty;
                if (!source.Contains("UkraineExact", StringComparison.OrdinalIgnoreCase)) continue;
                if (!_imageStates.ContainsKey(image))
                    _imageStates[image] = new ImageState(image.Visibility, image.Opacity);
                if (stalker)
                {
                    image.Visibility = Visibility.Collapsed;
                    image.Opacity = 0;
                }
                else if (_imageStates.TryGetValue(image, out var state))
                {
                    image.Visibility = state.Visibility;
                    image.Opacity = state.Opacity;
                }
            }
        }

        private void ApplyHeaderArtwork(bool stalker)
        {
            var header = FindHeaderGrid();
            if (header is null) return;

            _headerBackdrop ??= new Image
            {
                Source = new BitmapImage(new Uri(
                    "/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerExact/zone-header.png",
                    UriKind.Relative)),
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0.32,
                IsHitTestVisible = false,
                Tag = "StalkerSettingsHeaderBackdrop"
            };
            _headerSymbol ??= new Image
            {
                Source = new BitmapImage(new Uri(
                    "/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerExact/stalker-symbol.png",
                    UriKind.Relative)),
                Stretch = Stretch.Uniform,
                Width = 86,
                Height = 92,
                Margin = new Thickness(7, -5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.95,
                IsHitTestVisible = false,
                Tag = "StalkerSettingsHeaderSymbol"
            };

            if (_headerBackdrop.Parent is null)
            {
                Grid.SetColumnSpan(_headerBackdrop, Math.Max(1, header.ColumnDefinitions.Count));
                Panel.SetZIndex(_headerBackdrop, -2);
                header.Children.Insert(0, _headerBackdrop);
            }
            if (_headerSymbol.Parent is null)
            {
                Panel.SetZIndex(_headerSymbol, 5);
                header.Children.Add(_headerSymbol);
            }

            _headerBackdrop.Visibility = stalker ? Visibility.Visible : Visibility.Collapsed;
            _headerSymbol.Visibility = stalker ? Visibility.Visible : Visibility.Collapsed;

            foreach (var text in FindDescendants<TextBlock>(header))
            {
                if (text.Text == "TiHiY")
                {
                    text.Foreground = stalker ? Brush("#E2DDCE") : Brush("#F4F8FF");
                    text.FontFamily = stalker ? new FontFamily("Consolas") : new FontFamily("Segoe UI");
                }
                else if (text.Text.Contains("StreamControl Center", StringComparison.OrdinalIgnoreCase))
                {
                    text.Foreground = stalker ? Brush("#C8893B") : Brush("#F5A900");
                    text.FontFamily = stalker ? new FontFamily("Consolas") : new FontFamily("Segoe UI");
                }
                else if (text.Text.Contains("SETTINGS", StringComparison.OrdinalIgnoreCase))
                {
                    text.Text = stalker
                        ? "ZONE SETTINGS  •  НАЛАШТУВАННЯ"
                        : "SETTINGS  •  НАЛАШТУВАННЯ";
                    text.Foreground = stalker ? Brush("#82A56E") : Brush("#9ADFAE");
                }
            }
        }

        private void ApplyBorders(bool stalker)
        {
            foreach (var border in FindDescendants<Border>(_window))
            {
                if (ReferenceEquals(border, _window.Content)) continue;
                if (ContainsPlatformImage(border)) continue;
                if (!ShouldTheme(border)) continue;

                if (!_borderStates.ContainsKey(border))
                    _borderStates[border] = new BorderState(
                        border.Background,
                        border.BorderBrush,
                        border.BorderThickness,
                        border.CornerRadius);

                if (stalker)
                {
                    var selectedThemeCard = ContainsText(border, "Сталкер") || ContainsText(border, "Stalker");
                    border.Background = new SolidColorBrush(Color.FromArgb(
                        selectedThemeCard ? (byte)235 : (byte)220,
                        selectedThemeCard ? (byte)27 : (byte)10,
                        selectedThemeCard ? (byte)25 : (byte)13,
                        selectedThemeCard ? (byte)16 : (byte)9));
                    border.BorderBrush = selectedThemeCard
                        ? Brush("#D69A35")
                        : Brush("#655536");
                    if (border.BorderThickness.Left < 0.8)
                        border.BorderThickness = new Thickness(1);
                    if (border.CornerRadius.TopLeft > 3)
                        border.CornerRadius = new CornerRadius(2);
                }
                else if (_borderStates.TryGetValue(border, out var state))
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
            foreach (var control in FindDescendants<Control>(_window))
            {
                if (control is ListBoxItem && ContainsPlatformImage(control)) continue;
                if (control is not Button && control is not TextBox && control is not ComboBox && control is not ListBoxItem && control is not PasswordBox)
                    continue;

                if (!_controlStates.ContainsKey(control))
                    _controlStates[control] = new ControlState(
                        control.Background,
                        control.BorderBrush,
                        control.Foreground,
                        control.BorderThickness,
                        control.FontFamily);

                if (stalker)
                {
                    control.Foreground = Brush("#E6E2D4");
                    control.FontFamily = new FontFamily("Segoe UI");
                    if (control is Button button)
                    {
                        var danger = Equals(button.Content, "×") || ContainsText(button, "СКАСУВАТИ");
                        button.Background = ResourceBrush(
                            danger ? "StalkerDangerButtonBrush" : "StalkerButtonBrush",
                            Brush("#201D17"));
                        button.BorderBrush = danger ? Brush("#9A3826") : Brush("#74502A");
                    }
                    else
                    {
                        control.Background = Brush("#E80C0F0A");
                        control.BorderBrush = Brush("#625236");
                    }
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

        private void ApplyText(bool stalker)
        {
            foreach (var text in FindDescendants<TextBlock>(_window))
            {
                if (text.Text is null || text.Text.Length == 0) continue;
                if (!_textStates.ContainsKey(text))
                    _textStates[text] = new TextState(text.Foreground, text.FontFamily);

                if (stalker)
                {
                    if (IsHeader(text))
                    {
                        text.Foreground = Brush("#D69A35");
                        text.FontFamily = new FontFamily("Consolas");
                    }
                    else if (IsBlue(text.Foreground as SolidColorBrush))
                    {
                        text.Foreground = Brush("#A09A87");
                    }
                }
                else if (_textStates.TryGetValue(text, out var state))
                {
                    text.Foreground = state.Foreground;
                    text.FontFamily = state.FontFamily;
                }
            }
        }

        private Grid? FindHeaderGrid() =>
            FindDescendants<Grid>(_window).FirstOrDefault(grid =>
                FindDescendants<TextBlock>(grid).Any(x => x.Text == "TiHiY") &&
                FindDescendants<TextBlock>(grid).Any(x => x.Text.Contains("StreamControl Center", StringComparison.OrdinalIgnoreCase)));

        private static bool ShouldTheme(Border border)
        {
            if (border.ActualWidth < 40 || border.ActualHeight < 20) return false;
            var background = border.Background as SolidColorBrush;
            var outline = border.BorderBrush as SolidColorBrush;
            return IsBlue(background) || IsBlue(outline)
                || (border.ActualWidth > 170 && border.ActualHeight > 34 && border.ActualHeight < 420);
        }

        private static bool IsHeader(TextBlock text) =>
            text.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight()
            && text.FontSize >= 12
            && (text.Text.Contains("ТЕМА", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("ІНТЕРФЕЙС", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("API", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("БЕЗПЕКА", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("TWITCH", StringComparison.OrdinalIgnoreCase)
                || text.Text.Contains("ПІДКЛЮЧЕННЯ", StringComparison.OrdinalIgnoreCase));

        private static bool IsBlue(SolidColorBrush? brush) =>
            brush is not null && brush.Color.B > brush.Color.R + 18 && brush.Color.B > brush.Color.G + 3;

        private static bool ContainsPlatformImage(DependencyObject root) =>
            FindDescendants<Image>(root).Any(image =>
                (image.Source?.ToString() ?? string.Empty).Contains("/Assets/Platforms/", StringComparison.OrdinalIgnoreCase));

        private static bool ContainsText(DependencyObject root, string fragment) =>
            FindDescendants<TextBlock>(root).Any(text =>
                text.Text?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);

        private static Brush ResourceBrush(string key, Brush fallback) =>
            Application.Current.TryFindResource(key) as Brush ?? fallback;

        private static SolidColorBrush Brush(string value) =>
            new((Color)ColorConverter.ConvertFromString(value));

        private void Window_Closed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _window.Closed -= Window_Closed;
            App.Services.Theme.ThemeChanged -= Theme_ThemeChanged;
        }

        private sealed record BorderState(Brush Background, Brush BorderBrush, Thickness BorderThickness, CornerRadius CornerRadius);
        private sealed record ControlState(Brush Background, Brush BorderBrush, Brush Foreground, Thickness BorderThickness, FontFamily FontFamily);
        private sealed record TextState(Brush Foreground, FontFamily FontFamily);
        private sealed record ImageState(Visibility Visibility, double Opacity);
    }

    private static readonly ConditionalWeakTable<SettingsWindow, Controller> Controllers = new();

    public static IDisposable Attach(SettingsWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
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
                for (var index = 0; index < count; index++) stack.Push(VisualTreeHelper.GetChild(current, index));
            }
            catch { }
            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>()) stack.Push(child);
            }
            catch { }
        }
    }
}
