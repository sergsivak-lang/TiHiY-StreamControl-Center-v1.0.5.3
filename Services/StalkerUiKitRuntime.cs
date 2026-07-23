using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Theme-local PDA UI kit. Styles existing and dynamically generated dashboard
/// elements without changing the visual state of any other theme.
/// </summary>
internal static class StalkerUiKitBootstrap
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
            _ = StalkerUiKitRuntime.Attach(window);
    }
}

internal static class StalkerUiKitRuntime
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
        private static readonly SolidColorBrush Amber = Frozen(Color.FromRgb(222, 157, 31));
        private static readonly SolidColorBrush AmberSoft = Frozen(Color.FromRgb(133, 91, 28));
        private static readonly SolidColorBrush Text = Frozen(Color.FromRgb(226, 218, 194));
        private static readonly SolidColorBrush Muted = Frozen(Color.FromRgb(164, 151, 119));
        private static readonly SolidColorBrush PdaBlack = Frozen(Color.FromArgb(235, 7, 11, 10));
        private static readonly SolidColorBrush PdaRow = Frozen(Color.FromArgb(225, 10, 17, 16));
        private static readonly SolidColorBrush PdaTrack = Frozen(Color.FromRgb(4, 12, 13));
        private static readonly FontFamily PdaFont = new("Bahnschrift SemiCondensed, Consolas");

        private readonly MainWindow _window;
        private readonly Dictionary<Border, BorderState> _borders = new();
        private readonly Dictionary<Control, ControlState> _controls = new();
        private readonly Dictionary<TextBlock, TextState> _texts = new();
        private readonly Dictionary<ProgressBar, ProgressState> _progress = new();
        private readonly RoutedEventHandler _loadedHandler;
        private bool _active;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _loadedHandler = DescendantLoaded;
            _window.AddHandler(FrameworkElement.LoadedEvent, _loadedHandler, true);
            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            QueueApply();
        }

        private void ThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply() => _window.Dispatcher.BeginInvoke(
            new Action(ApplyThemeState), DispatcherPriority.ContextIdle);

        private void ApplyThemeState()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                _active = true;
                foreach (var element in StalkerApprovedAssets.FindDescendants<FrameworkElement>(_window).ToList())
                    StyleElement(element);
            }
            else if (_active)
            {
                Restore();
                _active = false;
            }
        }

        private void DescendantLoaded(object sender, RoutedEventArgs e)
        {
            if (!_active || e.OriginalSource is not FrameworkElement element) return;
            if (!IsInsideDashboard(element)) return;
            _window.Dispatcher.BeginInvoke(new Action(() => StyleElement(element)), DispatcherPriority.Background);
        }

        private bool IsInsideDashboard(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                if (current is MainWindow) return true;
                current = StalkerApprovedAssets.GetParent(current);
            }
            return false;
        }

        private void StyleElement(FrameworkElement element)
        {
            if (!_active || !IsDashboardElement(element)) return;

            switch (element)
            {
                case Border border:
                    StyleBorder(border);
                    break;
                case Button button:
                    StyleControl(button, true);
                    break;
                case TextBox textBox:
                    StyleControl(textBox, false);
                    textBox.CaretBrush = Amber;
                    textBox.SelectionBrush = AmberSoft;
                    break;
                case ComboBox comboBox:
                    StyleControl(comboBox, false);
                    break;
                case ListBox listBox:
                    StyleControl(listBox, false);
                    listBox.Background = Brushes.Transparent;
                    listBox.BorderThickness = new Thickness(0);
                    break;
                case ListBoxItem item:
                    StyleControl(item, false);
                    item.Background = Brushes.Transparent;
                    item.BorderThickness = new Thickness(0);
                    break;
                case ProgressBar bar:
                    StyleProgress(bar);
                    break;
                case TextBlock text:
                    StyleText(text);
                    break;
            }
        }

        private bool IsDashboardElement(FrameworkElement element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                if (current is FrameworkElement fe && fe.Name is
                    "ChatBlockPanel" or "DonationsBlockPanel" or "MixerBlockPanel" or
                    "NotificationsBlockPanel" or "SystemStatusBlockPanel" or
                    "SystemMonitorPanel" or "ModulesBlockPanel" or "DesignSurface")
                    return true;
                if (current is MainWindow) break;
                current = StalkerApprovedAssets.GetParent(current);
            }
            return false;
        }

        private void StyleBorder(Border border)
        {
            if (!_borders.ContainsKey(border))
                _borders[border] = new BorderState(
                    border.Background, border.BorderBrush, border.BorderThickness, border.CornerRadius);

            var dataRow = border.DataContext is ChatMessage or DonationEvent or AudioChannel;
            var compactBadge = border.ActualHeight is > 0 and < 48 && border.ActualWidth is > 0 and < 280;

            if (dataRow)
            {
                border.Background = PdaRow;
                border.BorderBrush = AmberSoft;
                border.BorderThickness = new Thickness(0, 0, 0, 1);
                border.CornerRadius = new CornerRadius(0);
                return;
            }

            if (compactBadge) return;

            if (border.Background is SolidColorBrush background && IsBlue(background.Color))
                border.Background = PdaBlack;
            if (border.BorderBrush is SolidColorBrush stroke && IsBlue(stroke.Color))
                border.BorderBrush = AmberSoft;
            if (border.BorderThickness != new Thickness(0))
                border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(Math.Min(2, border.CornerRadius.TopLeft));
        }

        private void StyleControl(Control control, bool button)
        {
            if (!_controls.ContainsKey(control))
                _controls[control] = new ControlState(
                    control.Background, control.BorderBrush, control.Foreground,
                    control.BorderThickness, control.FontFamily, control.FontWeight);

            control.FontFamily = PdaFont;
            control.Foreground = button ? Amber : Text;
            control.Background = button ? PdaBlack : Frozen(Color.FromArgb(230, 5, 13, 14));
            control.BorderBrush = AmberSoft;
            control.BorderThickness = button ? new Thickness(1) : control.BorderThickness;
            control.FontWeight = button ? FontWeights.SemiBold : control.FontWeight;
        }

        private void StyleText(TextBlock text)
        {
            if (!_texts.ContainsKey(text))
                _texts[text] = new TextState(text.Foreground, text.FontFamily, text.FontWeight);

            text.FontFamily = PdaFont;
            if (text.Foreground is SolidColorBrush brush && IsBlue(brush.Color))
                text.Foreground = Muted;
        }

        private void StyleProgress(ProgressBar bar)
        {
            if (!_progress.ContainsKey(bar))
                _progress[bar] = new ProgressState(bar.Background, bar.BorderBrush, bar.Foreground);

            bar.Background = PdaTrack;
            bar.BorderBrush = AmberSoft;
            if (bar.Foreground is SolidColorBrush brush && IsBlue(brush.Color))
                bar.Foreground = Amber;
        }

        private static bool IsBlue(Color color) =>
            color.B > color.R + 18 && color.B > color.G + 8;

        private void Restore()
        {
            foreach (var (border, state) in _borders)
            {
                border.Background = state.Background;
                border.BorderBrush = state.BorderBrush;
                border.BorderThickness = state.BorderThickness;
                border.CornerRadius = state.CornerRadius;
            }
            _borders.Clear();

            foreach (var (control, state) in _controls)
            {
                control.Background = state.Background;
                control.BorderBrush = state.BorderBrush;
                control.Foreground = state.Foreground;
                control.BorderThickness = state.BorderThickness;
                control.FontFamily = state.FontFamily;
                control.FontWeight = state.FontWeight;
            }
            _controls.Clear();

            foreach (var (text, state) in _texts)
            {
                text.Foreground = state.Foreground;
                text.FontFamily = state.FontFamily;
                text.FontWeight = state.FontWeight;
            }
            _texts.Clear();

            foreach (var (bar, state) in _progress)
            {
                bar.Background = state.Background;
                bar.BorderBrush = state.BorderBrush;
                bar.Foreground = state.Foreground;
            }
            _progress.Clear();
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.RemoveHandler(FrameworkElement.LoadedEvent, _loadedHandler);
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record BorderState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness, CornerRadius CornerRadius);
        private sealed record ControlState(Brush? Background, Brush? BorderBrush, Brush? Foreground, Thickness BorderThickness, FontFamily FontFamily, FontWeight FontWeight);
        private sealed record TextState(Brush? Foreground, FontFamily FontFamily, FontWeight FontWeight);
        private sealed record ProgressState(Brush? Background, Brush? BorderBrush, Brush? Foreground);
    }
}