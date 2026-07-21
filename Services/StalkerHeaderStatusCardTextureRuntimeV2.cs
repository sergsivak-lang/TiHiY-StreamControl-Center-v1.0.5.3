using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Gives the live OBS / multistream / Twitch / YouTube header cards the exact
/// approved STALKER frame and icon artwork while keeping every caption dynamic.
/// </summary>
internal static class StalkerHeaderStatusCardTextureV2Bootstrap
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
            _ = StalkerHeaderStatusCardTextureV2Runtime.Attach(window);
    }
}

internal static class StalkerHeaderStatusCardTextureV2Runtime
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
        private static readonly object WrapperMarker = new();

        // Pixel-perfect crops from the 1672 x 941 approved reference.
        private static readonly CardSpec[] Specs =
        {
            new("OBS AUDIO",   new Int32Rect(121, 87, 159, 52), 159, 14, 50),
            new("MULTISTREAM", new Int32Rect(294, 87, 185, 52), 185, 14, 52),
            new("TWITCH",      new Int32Rect(494, 87, 170, 52), 170, 15, 50),
            new("YOUTUBE",     new Int32Rect(679, 87, 170, 52), 170,  0, 50)
        };

        private readonly MainWindow _window;
        private readonly BitmapSource _approvedReference;
        private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Border, CardState> _cardStates = new();
        private readonly Dictionary<Button, ButtonState> _buttonStates = new();
        private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();
        private readonly Dictionary<TextBlock, TextState> _textStates = new();
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _approvedReference = StalkerApprovedAssets.Load("approved-full-reference.png");

            // Loaded class handlers run before Dispatcher work from the other theme runtimes,
            // so this captures the real Ukraine baseline for a clean theme switch back.
            CaptureTargets();

            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
        }

        private void ThemeChanged(object? sender, EventArgs e)
        {
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
        }

        private void CaptureTargets()
        {
            var statusRow = FindStatusRow();
            if (statusRow is null) return;

            foreach (var spec in Specs)
            {
                var title = StalkerApprovedAssets.FindDescendants<TextBlock>(statusRow)
                    .FirstOrDefault(x => string.Equals(x.Text, spec.Title, StringComparison.OrdinalIgnoreCase));
                if (title is null) continue;

                var border = FindAncestor<Border>(title, statusRow);
                if (border is null) continue;

                _cards[spec.Title] = border;
                CaptureCard(border);

                var button = FindAncestor<Button>(border, statusRow);
                if (button is not null) CaptureButton(button);

                CaptureElement(title);
                CaptureText(title);

                var textStack = FindAncestor<StackPanel>(title, border);
                if (textStack is not null) CaptureElement(textStack);

                if (border.Child is StackPanel root)
                {
                    CaptureElement(root);
                    foreach (var icon in root.Children.OfType<FrameworkElement>()
                                 .Where(x => x is Image || x is Ellipse))
                        CaptureElement(icon);
                }

                foreach (var text in StalkerApprovedAssets.FindDescendants<TextBlock>(border))
                    CaptureText(text);
            }
        }

        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
                ApplyCards();
            else if (_lastStalker)
                RestoreAll();

            _lastStalker = stalker;
        }

        private void ApplyCards()
        {
            foreach (var spec in Specs)
            {
                if (_cards.TryGetValue(spec.Title, out var border))
                    ApplyCard(border, spec);
            }
        }

        private void ApplyCard(Border border, CardSpec spec)
        {
            border.Width = spec.Width;
            border.Height = 52;
            border.MinWidth = 0;
            border.MinHeight = 0;
            border.Margin = new Thickness(0, 0, spec.RightGap, 0);
            border.Padding = new Thickness(0);
            border.Background = CreateCropBrush(spec.Crop);
            border.BorderBrush = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
            border.CornerRadius = new CornerRadius(0);
            border.HorizontalAlignment = HorizontalAlignment.Left;
            border.VerticalAlignment = VerticalAlignment.Center;

            var alreadyWrapped = border.Child is Grid currentWrapper &&
                                 ReferenceEquals(currentWrapper.Tag, WrapperMarker);

            if (!alreadyWrapped && _cardStates.TryGetValue(border, out var state))
            {
                var originalChild = state.Child;
                border.Child = null;

                var wrapper = new Grid
                {
                    Tag = WrapperMarker,
                    ClipToBounds = true,
                    Background = Brushes.Transparent,
                    IsHitTestVisible = true
                };

                // Leave the exact approved icon and frame visible. Only cover the mock-up's
                // painted words; the application's real status text is drawn above this layer.
                wrapper.Children.Add(new Border
                {
                    Margin = new Thickness(spec.TextLeft - 5, 2, 2, 2),
                    CornerRadius = new CornerRadius(2),
                    Background = StalkerApprovedAssets.NewTiledBrush(
                        "panel-fill-dark.png", 220, 48, 1.0),
                    IsHitTestVisible = false
                });

                if (originalChild is not null)
                    wrapper.Children.Add(originalChild);

                border.Child = wrapper;
            }

            var statusRow = FindStatusRow();
            if (statusRow is not null && FindAncestor<Button>(border, statusRow) is { } button)
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
                button.BorderThickness = new Thickness(0);
                button.Padding = new Thickness(0);
                button.Margin = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.VerticalContentAlignment = VerticalAlignment.Center;
            }

            var title = StalkerApprovedAssets.FindDescendants<TextBlock>(border)
                .FirstOrDefault(x => string.Equals(x.Text, spec.Title, StringComparison.OrdinalIgnoreCase));
            if (title is null) return;

            var textStack = FindAncestor<StackPanel>(title, border);
            if (textStack is not null)
            {
                textStack.Margin = new Thickness(spec.TextLeft, 0, 4, 0);
                textStack.HorizontalAlignment = HorizontalAlignment.Left;
                textStack.VerticalAlignment = VerticalAlignment.Center;
            }

            var root = GetOriginalRoot(border);
            if (root is not null)
            {
                root.Margin = new Thickness(0);
                root.HorizontalAlignment = HorizontalAlignment.Stretch;
                root.VerticalAlignment = VerticalAlignment.Center;

                foreach (var icon in root.Children.OfType<FrameworkElement>()
                             .Where(x => x is Image || x is Ellipse))
                {
                    icon.Opacity = 0;
                    icon.IsHitTestVisible = false;
                }
            }

            title.Foreground = new SolidColorBrush(Color.FromRgb(226, 218, 194));
            title.FontFamily = new FontFamily("Consolas");
            title.FontWeight = FontWeights.Bold;
            title.FontSize = 11.5;

            var status = StalkerApprovedAssets.FindDescendants<TextBlock>(border)
                .FirstOrDefault(x => !ReferenceEquals(x, title) && x.Visibility == Visibility.Visible);
            if (status is not null)
            {
                status.FontFamily = new FontFamily("Consolas");
                status.FontWeight = FontWeights.Bold;
                status.FontSize = 10.5;
            }
        }

        private StackPanel? GetOriginalRoot(Border border)
        {
            if (_cardStates.TryGetValue(border, out var state))
                return state.Child as StackPanel;
            return null;
        }

        private ImageBrush CreateCropBrush(Int32Rect rect)
        {
            var cropped = new CroppedBitmap(_approvedReference, rect);
            cropped.Freeze();

            var brush = new ImageBrush(cropped)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };
            brush.Freeze();
            return brush;
        }

        private void RestoreAll()
        {
            foreach (var pair in _cardStates)
            {
                var border = pair.Key;
                var state = pair.Value;

                if (border.Child is Grid wrapper && ReferenceEquals(wrapper.Tag, WrapperMarker))
                {
                    if (state.Child is not null)
                        wrapper.Children.Remove(state.Child);
                    border.Child = state.Child;
                }

                border.Background = state.Background;
                border.BorderBrush = state.BorderBrush;
                border.BorderThickness = state.BorderThickness;
                border.CornerRadius = state.CornerRadius;
                border.Padding = state.Padding;
                border.Margin = state.Margin;
                border.Width = state.Width;
                border.Height = state.Height;
                border.MinWidth = state.MinWidth;
                border.MinHeight = state.MinHeight;
                border.HorizontalAlignment = state.HorizontalAlignment;
                border.VerticalAlignment = state.VerticalAlignment;
            }

            foreach (var pair in _buttonStates)
            {
                var button = pair.Key;
                var state = pair.Value;
                button.Background = state.Background;
                button.BorderBrush = state.BorderBrush;
                button.BorderThickness = state.BorderThickness;
                button.Padding = state.Padding;
                button.Margin = state.Margin;
                button.HorizontalContentAlignment = state.HorizontalContentAlignment;
                button.VerticalContentAlignment = state.VerticalContentAlignment;
            }

            foreach (var pair in _elementStates)
            {
                var element = pair.Key;
                var state = pair.Value;
                element.Margin = state.Margin;
                element.HorizontalAlignment = state.HorizontalAlignment;
                element.VerticalAlignment = state.VerticalAlignment;
                element.Opacity = state.Opacity;
                element.Visibility = state.Visibility;
                element.IsHitTestVisible = state.IsHitTestVisible;
            }

            foreach (var pair in _textStates)
            {
                var text = pair.Key;
                var state = pair.Value;
                text.Foreground = state.Foreground;
                text.FontFamily = state.FontFamily;
                text.FontWeight = state.FontWeight;
                text.FontSize = state.FontSize;
            }

            _window.InvalidateMeasure();
            _window.InvalidateArrange();
            _window.InvalidateVisual();
        }

        private Grid? FindStatusRow()
        {
            var design = StalkerApprovedAssets.FindDescendants<Grid>(_window)
                .FirstOrDefault(x => string.Equals(x.Name, "DesignSurface", StringComparison.Ordinal));
            return design?.Children.OfType<Grid>().FirstOrDefault(x => Grid.GetRow(x) == 1);
        }

        private static T? FindAncestor<T>(DependencyObject current, DependencyObject stopAt)
            where T : DependencyObject
        {
            var parent = StalkerApprovedAssets.GetParent(current);
            while (parent is not null && !ReferenceEquals(parent, stopAt))
            {
                if (parent is T match) return match;
                parent = StalkerApprovedAssets.GetParent(parent);
            }
            return null;
        }

        private void CaptureCard(Border border)
        {
            if (_cardStates.ContainsKey(border)) return;
            _cardStates[border] = new CardState(
                border.Child,
                border.Background,
                border.BorderBrush,
                border.BorderThickness,
                border.CornerRadius,
                border.Padding,
                border.Margin,
                border.Width,
                border.Height,
                border.MinWidth,
                border.MinHeight,
                border.HorizontalAlignment,
                border.VerticalAlignment);
        }

        private void CaptureButton(Button button)
        {
            if (_buttonStates.ContainsKey(button)) return;
            _buttonStates[button] = new ButtonState(
                button.Background,
                button.BorderBrush,
                button.BorderThickness,
                button.Padding,
                button.Margin,
                button.HorizontalContentAlignment,
                button.VerticalContentAlignment);
        }

        private void CaptureElement(FrameworkElement element)
        {
            if (_elementStates.ContainsKey(element)) return;
            _elementStates[element] = new ElementState(
                element.Margin,
                element.HorizontalAlignment,
                element.VerticalAlignment,
                element.Opacity,
                element.Visibility,
                element.IsHitTestVisible);
        }

        private void CaptureText(TextBlock text)
        {
            if (_textStates.ContainsKey(text)) return;
            _textStates[text] = new TextState(
                text.Foreground,
                text.FontFamily,
                text.FontWeight,
                text.FontSize);
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record CardSpec(
            string Title,
            Int32Rect Crop,
            double Width,
            double RightGap,
            double TextLeft);

        private sealed record CardState(
            UIElement? Child,
            Brush? Background,
            Brush? BorderBrush,
            Thickness BorderThickness,
            CornerRadius CornerRadius,
            Thickness Padding,
            Thickness Margin,
            double Width,
            double Height,
            double MinWidth,
            double MinHeight,
            HorizontalAlignment HorizontalAlignment,
            VerticalAlignment VerticalAlignment);

        private sealed record ButtonState(
            Brush? Background,
            Brush? BorderBrush,
            Thickness BorderThickness,
            Thickness Padding,
            Thickness Margin,
            HorizontalAlignment HorizontalContentAlignment,
            VerticalAlignment VerticalContentAlignment);

        private sealed record ElementState(
            Thickness Margin,
            HorizontalAlignment HorizontalAlignment,
            VerticalAlignment VerticalAlignment,
            double Opacity,
            Visibility Visibility,
            bool IsHitTestVisible);

        private sealed record TextState(
            Brush? Foreground,
            FontFamily FontFamily,
            FontWeight FontWeight,
            double FontSize);
    }
}
