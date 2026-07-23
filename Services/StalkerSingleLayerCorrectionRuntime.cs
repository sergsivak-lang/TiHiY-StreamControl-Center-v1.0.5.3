using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Final, idempotent STALKER-only correction pass.
/// Keeps the normal responsive dashboard geometry and removes visual collisions
/// left by earlier experimental texture runtimes.
/// </summary>
internal static class StalkerSingleLayerCorrectionBootstrap
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
            _ = StalkerSingleLayerCorrectionRuntime.Attach(window);
    }
}

internal static class StalkerSingleLayerCorrectionRuntime
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
        private static readonly string[] DuplicateHeaderFragments =
        {
            "МУЛЬТИЧАТ", "ДОНАТИ", "ШВИДКИЙ МІКШЕР", "AUDIO MIXER OBS",
            "СПОВІЩЕННЯ", "СТАН СИСТЕМИ", "AIDA64 LIVE"
        };

        private readonly MainWindow _window;
        private readonly Dictionary<UIElement, double> _opacity = new();
        private readonly Dictionary<Border, Brush?> _borderBrushes = new();
        private readonly Dictionary<Border, Thickness> _borderThicknesses = new();
        private readonly Dictionary<Button, ButtonState> _buttonStates = new();
        private ContentControl? _center;
        private object? _originalCenterContent;
        private Brush? _originalCenterBackground;
        private Thickness _originalCenterPadding;
        private bool _centerCaptured;
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            QueueApply();
        }

        private void ThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply() => _window.Dispatcher.BeginInvoke(
            new Action(Apply), DispatcherPriority.ContextIdle);

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                ApplyResponsiveGeometry();
                HideDuplicateHeadings();
                NeutralizeBluePanelFrames();
                ApplyStalkerButtons();
                BuildCenterModule();
            }
            else if (_lastStalker)
            {
                Restore();
            }
            _lastStalker = stalker;
        }

        private void ApplyResponsiveGeometry()
        {
            var design = FindNamed<Grid>("DesignSurface");
            if (design is not null)
            {
                design.Margin = new Thickness(8, 0, 8, 12);
                design.UseLayoutRounding = true;
                design.SnapsToDevicePixels = true;
            }

            // Never lock the live window to the mock-up's pixel dimensions.
            _window.MinWidth = 1200;
            _window.MinHeight = 700;
            _window.MaxWidth = double.PositiveInfinity;
            _window.MaxHeight = double.PositiveInfinity;
        }

        private void HideDuplicateHeadings()
        {
            foreach (var text in Descendants<TextBlock>(_window))
            {
                var value = text.Text ?? string.Empty;
                if (!DuplicateHeaderFragments.Any(fragment =>
                        value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!_opacity.ContainsKey(text)) _opacity[text] = text.Opacity;
                text.Opacity = 0;
                text.IsHitTestVisible = false;
            }
        }

        private void NeutralizeBluePanelFrames()
        {
            foreach (var name in new[]
                     {
                         "ChatBlockPanel", "DonationsBlockPanel", "MixerBlockPanel",
                         "NotificationsBlockPanel", "SystemStatusBlockPanel", "SystemMonitorPanel"
                     })
            {
                var panel = FindNamed<ContentControl>(name);
                if (panel is null) continue;

                foreach (var border in Descendants<Border>(panel))
                {
                    // Preserve colored platform/status badges and list rows.
                    if (border.DataContext is Models.ChatMessage or Models.DonationEvent) continue;
                    if (border.ActualHeight > 0 && border.ActualHeight < 55 && border.ActualWidth < 260) continue;

                    if (!_borderBrushes.ContainsKey(border))
                    {
                        _borderBrushes[border] = border.BorderBrush;
                        _borderThicknesses[border] = border.BorderThickness;
                    }
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(113, 78, 31));
                    border.BorderThickness = border.BorderThickness == new Thickness(0)
                        ? new Thickness(0)
                        : new Thickness(1);
                }
            }
        }

        private void ApplyStalkerButtons()
        {
            foreach (var button in Descendants<Button>(_window))
            {
                if (!_buttonStates.ContainsKey(button))
                    _buttonStates[button] = new ButtonState(
                        button.Background, button.BorderBrush, button.Foreground,
                        button.BorderThickness, button.FontFamily, button.FontWeight);

                button.Background = new SolidColorBrush(Color.FromArgb(220, 13, 16, 14));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(176, 119, 25));
                button.BorderThickness = new Thickness(1);
                button.Foreground = new SolidColorBrush(Color.FromRgb(236, 184, 56));
                button.FontFamily = new FontFamily("Bahnschrift SemiCondensed, Consolas");
                button.FontWeight = FontWeights.SemiBold;
            }
        }

        private void BuildCenterModule()
        {
            var footer = FindNamed<Grid>("FooterBlocksGrid");
            _center ??= FindNamed<ContentControl>("ModulesBlockPanel")
                       ?? footer?.Children.OfType<ContentControl>()
                           .FirstOrDefault(x => Grid.GetColumn(x) == 2);
            if (_center is null) return;

            if (!_centerCaptured)
            {
                _originalCenterContent = _center.Content;
                _originalCenterBackground = _center.Background;
                _originalCenterPadding = _center.Padding;
                _centerCaptured = true;
            }

            var root = new Grid
            {
                ClipToBounds = true,
                Margin = new Thickness(0),
                Background = new ImageBrush(StalkerApprovedAssets.Load("center-zone-panel-exact.png"))
                {
                    Stretch = Stretch.Fill,
                    Opacity = 1
                }
            };

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var centerStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 12, 18, 6)
            };
            centerStack.Children.Add(new TextBlock
            {
                Text = "☢  ZONE CONTROL",
                Foreground = new SolidColorBrush(Color.FromRgb(239, 179, 41)),
                FontFamily = new FontFamily("Bahnschrift SemiCondensed, Consolas"),
                FontSize = 25,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            centerStack.Children.Add(new TextBlock
            {
                Text = "PDA • СТАН ЗОНИ • МОДУЛІ КЕРУВАННЯ",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 190, 153)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetRow(centerStack, 0);
            root.Children.Add(centerStack);

            var footerText = new TextBlock
            {
                Text = "ЗВ'ЯЗОК ІЗ ЗОНОЮ: СТАБІЛЬНИЙ",
                Foreground = new SolidColorBrush(Color.FromRgb(96, 205, 91)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(footerText, 1);
            root.Children.Add(footerText);

            _center.Content = root;
            _center.Background = Brushes.Transparent;
            _center.BorderBrush = Brushes.Transparent;
            _center.BorderThickness = new Thickness(0);
            _center.Padding = new Thickness(0);
            _center.Opacity = 1;
            _center.Visibility = Visibility.Visible;
        }

        private void Restore()
        {
            foreach (var (element, opacity) in _opacity)
            {
                element.Opacity = opacity;
                element.IsHitTestVisible = true;
            }
            _opacity.Clear();

            foreach (var (border, brush) in _borderBrushes)
            {
                border.BorderBrush = brush;
                border.BorderThickness = _borderThicknesses[border];
            }
            _borderBrushes.Clear();
            _borderThicknesses.Clear();

            foreach (var (button, state) in _buttonStates)
            {
                button.Background = state.Background;
                button.BorderBrush = state.BorderBrush;
                button.Foreground = state.Foreground;
                button.BorderThickness = state.BorderThickness;
                button.FontFamily = state.FontFamily;
                button.FontWeight = state.FontWeight;
            }
            _buttonStates.Clear();

            if (_centerCaptured && _center is not null)
            {
                _center.Content = _originalCenterContent;
                _center.Background = _originalCenterBackground;
                _center.Padding = _originalCenterPadding;
            }
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            Descendants<T>(_window).FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.Ordinal));

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject =>
            StalkerApprovedAssets.FindDescendants<T>(root);

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record ButtonState(
            Brush? Background,
            Brush? BorderBrush,
            Brush? Foreground,
            Thickness BorderThickness,
            FontFamily FontFamily,
            FontWeight FontWeight);
    }
}
