using System.Collections.Specialized;

namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerThemeRuntimeBootstrap
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
            _ = StalkerThemeRuntime.Attach(window);
    }
}

public static class StalkerThemeRuntime
{
    private sealed class Controller : IDisposable
    {
        private const string Marker = "TiHiY.StalkerRuntime";
        private readonly MainWindow _window;
        private readonly DispatcherTimer _guard;
        private readonly Dictionary<Control, ControlSnapshot> _controlSnapshots = new();
        private readonly Dictionary<Border, BorderSnapshot> _borderSnapshots = new();
        private readonly Dictionary<TextBlock, TextSnapshot> _textSnapshots = new();
        private bool _disposed;
        private bool _lastStalkerState;
        private Image? _headerBackdrop;
        private Image? _headerSymbol;

        private static readonly string[] DashboardPanelNames =
        {
            "ChatBlockPanel", "DonationsBlockPanel", "MixerBlockPanel",
            "NotificationsBlockPanel", "SystemStatusBlockPanel", "SystemMonitorPanel"
        };

        private static readonly string[] DashboardHeaders =
        {
            "МУЛЬТИЧАТ • TWITCH + YOUTUBE", "ДОНАТИ",
            "ШВИДКИЙ МІКШЕР • AUDIO MIXER OBS", "СПОВІЩЕННЯ",
            "СТАН СИСТЕМИ", "AIDA64 LIVE"
        };

        public Controller(MainWindow window)
        {
            _window = window;
            _window.Closed += Window_Closed;
            App.Services.Theme.ThemeChanged += Theme_ThemeChanged;
            _guard = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _guard.Tick += Guard_Tick;
            _guard.Start();

            if (Environment.GetCommandLineArgs().Any(x =>
                    string.Equals(x, "--ci-apply-stalker-theme", StringComparison.OrdinalIgnoreCase)))
                App.Services.Theme.Apply("Сталкер", save: false);

            window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Loaded);
        }

        private bool IsStalker => string.Equals(
            App.Services.Theme.CurrentTheme,
            "Сталкер",
            StringComparison.OrdinalIgnoreCase);

        private void Guard_Tick(object? sender, EventArgs e)
        {
            if (_disposed || !_window.IsLoaded) return;
            if (IsStalker || _lastStalkerState)
                ApplyNow();
        }

        private void Theme_ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);

        public void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = IsStalker;
            ApplyWindowSurface(stalker);
            ApplyDashboardPanels(stalker);
            ApplyHeaders(stalker);
            ApplyControls(stalker);
            ApplyStatusCards(stalker);
            ApplyHeaderArtwork(stalker);
            ApplyCenterBanner(stalker);
            ApplyTitle(stalker);
            _lastStalkerState = stalker;
        }

        private void ApplyWindowSurface(bool stalker)
        {
            _window.Background = BrushResource(stalker ? "StalkerWindowBrush" : "UkraineWindowBrush", Brushes.Black);
            if (_window.Content is Border root)
            {
                root.Background = BrushResource(stalker ? "StalkerWindowBrush" : "UkraineWindowBrush", Brushes.Black);
                root.BorderBrush = stalker ? Brush("#9A571F") : Brush("#0FA5EA");
                root.BorderThickness = stalker ? new Thickness(2) : new Thickness(1);
            }

            if (FindNamed<Grid>("DesignSurface") is { } design)
            {
                design.Background = stalker
                    ? new SolidColorBrush(Color.FromArgb(34, 77, 72, 49))
                    : Brushes.Transparent;
            }
        }

        private void ApplyDashboardPanels(bool stalker)
        {
            var style = StyleResource(stalker ? "StalkerHudPanel" : "HudPanel");
            foreach (var name in DashboardPanelNames)
            {
                if (FindNamed<ContentControl>(name) is not { } panel) continue;
                panel.Style = style;
                panel.Foreground = BrushResource(stalker ? "StalkerTextBrush" : "Text", Brushes.White);
                panel.Padding = name == "SystemStatusBlockPanel"
                    ? new Thickness(stalker ? 48 : 38, panel.Padding.Top, panel.Padding.Right, panel.Padding.Bottom)
                    : new Thickness(stalker ? 42 : 38, panel.Padding.Top, panel.Padding.Right, panel.Padding.Bottom);
            }
        }

        private void ApplyHeaders(bool stalker)
        {
            var style = StyleResource(stalker ? "StalkerHeaderText" : "UkraineHeaderText");
            foreach (var text in FindDescendants<TextBlock>(_window))
            {
                if (!DashboardHeaders.Contains(text.Text, StringComparer.OrdinalIgnoreCase)) continue;
                if (!_textSnapshots.ContainsKey(text))
                    _textSnapshots[text] = new TextSnapshot(text.Foreground, text.FontFamily, text.FontSize, text.FontWeight, text.Text);
                text.Style = style;
                text.Foreground = stalker ? Brush("#D69A35") : Brush("#F5A900");
                text.FontFamily = stalker ? new FontFamily("Consolas") : new FontFamily("Segoe UI");
                text.FontWeight = FontWeights.Bold;
                text.TextEffects = stalker ? BuildGlow(Color.FromRgb(104, 86, 43)) : null;
            }
        }

        private void ApplyControls(bool stalker)
        {
            foreach (var button in FindDescendants<Button>(_window))
            {
                Remember(button);
                if (stalker)
                {
                    var danger = Equals(button.Content, "×") || button.Name.Contains("Stop", StringComparison.OrdinalIgnoreCase);
                    button.Foreground = Brush("#E6E2D4");
                    button.Background = BrushResource(danger ? "StalkerDangerButtonBrush" : "StalkerButtonBrush", Brush("#201D17"));
                    button.BorderBrush = danger ? Brush("#A33B27") : Brush("#74502A");
                    button.BorderThickness = new Thickness(1.35);
                    button.FontFamily = new FontFamily("Segoe UI");
                }
                else Restore(button);
            }

            foreach (var box in FindDescendants<TextBox>(_window))
            {
                Remember(box);
                if (stalker)
                {
                    box.Background = Brush("#E8080A07");
                    box.BorderBrush = Brush("#665437");
                    box.Foreground = Brush("#E6E2D4");
                    box.CaretBrush = Brush("#78C83C");
                    box.SelectionBrush = Brush("#805F8D32");
                }
                else Restore(box);
            }

            foreach (var combo in FindDescendants<ComboBox>(_window))
            {
                Remember(combo);
                if (stalker)
                {
                    combo.Background = Brush("#E814160F");
                    combo.BorderBrush = Brush("#665437");
                    combo.Foreground = Brush("#E6E2D4");
                }
                else Restore(combo);
            }

            foreach (var progress in FindDescendants<ProgressBar>(_window))
            {
                Remember(progress);
                if (stalker)
                {
                    progress.Background = Brush("#080A07");
                    progress.BorderBrush = Brush("#5D5237");
                    progress.Foreground = new LinearGradientBrush(
                        Color.FromRgb(73, 164, 57),
                        Color.FromRgb(190, 143, 45),
                        0);
                }
                else Restore(progress);
            }
        }

        private void ApplyStatusCards(bool stalker)
        {
            foreach (var border in FindDescendants<Border>(_window))
            {
                if (ReferenceEquals(border, _window.Content)) continue;
                if (border.Child is not StackPanel && border.Child is not Grid) continue;
                if (border.ActualHeight is < 24 or > 110) continue;
                Remember(border);
                if (stalker)
                {
                    var current = border.BorderBrush as SolidColorBrush;
                    var isBrand = current is not null && (current.Color.R > 120 || current.Color.B > 120);
                    border.Background = Brush("#D313150F");
                    border.BorderBrush = isBrand ? current : Brush("#665638");
                    if (border.CornerRadius.TopLeft > 3)
                        border.CornerRadius = new CornerRadius(2);
                }
                else Restore(border);
            }
        }

        private void ApplyHeaderArtwork(bool stalker)
        {
            var header = FindHeaderGrid();
            if (header is null) return;

            _headerBackdrop ??= CreateHeaderImage(
                "/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerExact/zone-header.png",
                Stretch.UniformToFill,
                HorizontalAlignment.Stretch,
                0.33,
                Marker + ".Header");
            _headerSymbol ??= CreateHeaderImage(
                "/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerExact/stalker-symbol.png",
                Stretch.Uniform,
                HorizontalAlignment.Left,
                0.94,
                Marker + ".Symbol");

            if (_headerBackdrop.Parent is null)
            {
                Grid.SetColumnSpan(_headerBackdrop, 3);
                Panel.SetZIndex(_headerBackdrop, -3);
                header.Children.Insert(0, _headerBackdrop);
            }
            if (_headerSymbol.Parent is null)
            {
                _headerSymbol.Width = 118;
                _headerSymbol.Height = 124;
                _headerSymbol.Margin = new Thickness(5, -14, 0, 0);
                _headerSymbol.VerticalAlignment = VerticalAlignment.Top;
                Panel.SetZIndex(_headerSymbol, 4);
                header.Children.Add(_headerSymbol);
            }

            _headerBackdrop.Visibility = stalker ? Visibility.Visible : Visibility.Collapsed;
            _headerSymbol.Visibility = stalker ? Visibility.Visible : Visibility.Collapsed;

            foreach (var title in FindDescendants<TextBlock>(header))
            {
                if (title.Text == "TiHiY")
                {
                    title.Foreground = stalker ? Brush("#DED8C7") : Brush("#F2F5F9");
                    title.FontFamily = stalker ? new FontFamily("Consolas") : new FontFamily("Segoe UI");
                }
                else if (title.Text.Contains("StreamControl Center", StringComparison.OrdinalIgnoreCase))
                {
                    title.Foreground = stalker ? Brush("#C8893B") : Brush("#F5A900");
                    title.FontFamily = stalker ? new FontFamily("Consolas") : new FontFamily("Segoe UI");
                }
                else if (title.Text.Contains("MULTICHAT", StringComparison.OrdinalIgnoreCase) &&
                         title.Text.Contains("MUSIC", StringComparison.OrdinalIgnoreCase))
                {
                    title.Text = stalker
                        ? "S.T.A.L.K.E.R.  •  ZONE CONTROL PANEL  •  SURVIVAL LINK"
                        : "MULTICHAT  •  DONATIONS  •  OBS AUDIO  •  MUSIC";
                    title.Foreground = stalker ? Brush("#8EA374") : Brush("#7E91A2");
                }
            }
        }

        private void ApplyCenterBanner(bool stalker)
        {
            var desired = stalker
                ? "/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerExact/zone-banner.png"
                : "/TiHiY.StreamControlCenter;component/Assets/Themes/UkraineExact/central-glory.png";

            foreach (var image in FindDescendants<Image>(_window))
            {
                var source = image.Source?.ToString() ?? string.Empty;
                if (!source.Contains("central-glory", StringComparison.OrdinalIgnoreCase) &&
                    !source.Contains("zone-banner", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(source, desired, StringComparison.OrdinalIgnoreCase)) continue;
                image.Source = new BitmapImage(new Uri(desired, UriKind.Relative));
                image.Stretch = Stretch.UniformToFill;
                if (image.Parent is ContentControl content)
                    content.Tag = "ExactCenterTexture";
            }
        }

        private void ApplyTitle(bool stalker)
        {
            _window.Title = stalker
                ? "TiHiY StreamControl Center — STALKER Zone"
                : "TiHiY StreamControl Center — Ukraine";
        }

        private Grid? FindHeaderGrid()
        {
            return FindDescendants<Grid>(_window).FirstOrDefault(grid =>
                FindDescendants<TextBlock>(grid).Any(x => x.Text == "TiHiY") &&
                FindDescendants<TextBlock>(grid).Any(x => x.Text.Contains("StreamControl Center", StringComparison.OrdinalIgnoreCase)));
        }

        private static Image CreateHeaderImage(string source, Stretch stretch, HorizontalAlignment alignment, double opacity, string tag) =>
            new()
            {
                Source = new BitmapImage(new Uri(source, UriKind.Relative)),
                Stretch = stretch,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = opacity,
                IsHitTestVisible = false,
                Tag = tag
            };

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            FindDescendants<T>(_window).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

        private static Style StyleResource(string key) =>
            Application.Current.TryFindResource(key) as Style
            ?? throw new InvalidOperationException($"Theme style resource is missing: {key}");

        private static Brush BrushResource(string key, Brush fallback) =>
            Application.Current.TryFindResource(key) as Brush ?? fallback;

        private static SolidColorBrush Brush(string value) =>
            new((Color)ColorConverter.ConvertFromString(value));

        private static TextEffectCollection BuildGlow(Color color) =>
            new()
            {
                new TextEffect
                {
                    Foreground = new SolidColorBrush(color),
                    PositionStart = 0,
                    PositionCount = 0
                }
            };

        private void Remember(Control control)
        {
            if (_controlSnapshots.ContainsKey(control)) return;
            _controlSnapshots[control] = new ControlSnapshot(
                control.Background,
                control.BorderBrush,
                control.Foreground,
                control.BorderThickness,
                control.FontFamily);
        }

        private void Restore(Control control)
        {
            if (!_controlSnapshots.TryGetValue(control, out var value)) return;
            control.Background = value.Background;
            control.BorderBrush = value.BorderBrush;
            control.Foreground = value.Foreground;
            control.BorderThickness = value.BorderThickness;
            control.FontFamily = value.FontFamily;
        }

        private void Remember(Border border)
        {
            if (_borderSnapshots.ContainsKey(border)) return;
            _borderSnapshots[border] = new BorderSnapshot(border.Background, border.BorderBrush, border.BorderThickness, border.CornerRadius);
        }

        private void Restore(Border border)
        {
            if (!_borderSnapshots.TryGetValue(border, out var value)) return;
            border.Background = value.Background;
            border.BorderBrush = value.BorderBrush;
            border.BorderThickness = value.BorderThickness;
            border.CornerRadius = value.CornerRadius;
        }

        private void Window_Closed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _guard.Stop();
            _guard.Tick -= Guard_Tick;
            _window.Closed -= Window_Closed;
            App.Services.Theme.ThemeChanged -= Theme_ThemeChanged;
        }

        private sealed record ControlSnapshot(
            Brush Background,
            Brush BorderBrush,
            Brush Foreground,
            Thickness BorderThickness,
            FontFamily FontFamily);

        private sealed record BorderSnapshot(
            Brush Background,
            Brush BorderBrush,
            Thickness BorderThickness,
            CornerRadius CornerRadius);

        private sealed record TextSnapshot(
            Brush Foreground,
            FontFamily FontFamily,
            double FontSize,
            FontWeight FontWeight,
            string Text);
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
}