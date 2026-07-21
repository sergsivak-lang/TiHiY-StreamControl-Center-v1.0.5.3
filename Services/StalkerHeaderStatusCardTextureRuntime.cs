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
/// Applies the four approved STALKER header status-card frames to the real live
/// OBS / multistream / Twitch / YouTube controls. The card captions remain live;
/// only the approved frame/icon artwork is taken from the approved reference.
/// </summary>
internal static class StalkerHeaderStatusCardTextureBootstrap
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
            _ = StalkerHeaderStatusCardTextureRuntime.Attach(window);
    }
}

internal static class StalkerHeaderStatusCardTextureRuntime
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

        // Exact card rectangles in Assets/Themes/StalkerApproved/approved-full-reference.png.
        // They contain the approved rusty frame and platform icon. The painted caption area
        // is covered with the approved dark panel texture and the real live captions stay on top.
        private static readonly CardSpec[] Specs =
        {
            new("OBS AUDIO",   new Int32Rect(121, 87, 159, 52), 159, 14, 50),
            new("MULTISTREAM", new Int32Rect(294, 87, 185, 52), 185, 14, 52),
            new("TWITCH",      new Int32Rect(494, 87, 170, 52), 170, 15, 50),
            new("YOUTUBE",     new Int32Rect(679, 87, 170, 52), 170,  0, 50)
        };

        private readonly MainWindow _window;
        private readonly BitmapSource _approvedReference;
        private readonly Dictionary<Border, CardState> _cardStates = new();
        private readonly Dictionary<Button, ButtonState> _buttonStates = new();
        private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();
        private readonly Dictionary<TextBlock, TextState> _textStates = new();
        private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _approvedReference = StalkerApprovedAssets.Load("approved-full-reference.png");

            // Capture the Ukraine baseline synchronously during Loaded, before the existing
            // approved STALKER runtime posts its own visual changes to the Dispatcher.
            CaptureTargets();

            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
        }

        private void ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);

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
                var textStack = FindAncestor<StackPanel>(title, border);
                if (textStack is not null) CaptureElement(textStack);

                if (border.Child is StackPanel root)
                {
                    CaptureElement(root);
                    foreach (var icon in root.Children.OfType<FrameworkElement>().Where(x => x is Image or Ellipse))
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
                if (!_cards.TryGetValue(spec.Title, out var border)) continue;
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

            if (_cardStates.TryGetValue(border, out var state) &&
                border.Child is not Grid { Tag: var tag } || !ReferenceEquals(tag, WrapperMarker))
            {
                var originalChild = state.Child as UIElement;
                border.Child = null;

                var wrapper = new Grid
                {
                    Tag = WrapperMarker,
                    ClipToBounds = true,
                    Background = Brushes.Transparent,
                    IsHitTestVisible = true
                };

                // Preserve the exact frame and approved icon on the left, but cover the
                // reference's painted words so the real connection state remains dynamic.
                wrapper.Children.Add(new Border
                {
                    Margin = new Thickness(spec.TextLeft - 5, 2, 2, 2),
                    CornerRadius = new CornerRadius(2),
                    Background = StalkerApprovedAssets.NewTiledBrush("panel-fill-dark.png", 220, 48, 1.0),
                    IsHitTestVisible = false
                });

                if (originalChild is not null)
                    wrapper.Children.Add(originalChild);

                border.Child = wrapper;
            }

            if (FindAncestor<Button>(border, FindStatusRow()) is { } button)
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
                textStack.VerticalAlignment = VerticalAlignment.Center;
                textStack.HorizontalAlignment = HorizontalAlignment.Left;
            }

            var root = border.Child is Grid wrapper
                ? wrapper.Children.OfType<StackPanel>().FirstOrDefault()
                : border.Child as StackPanel;

            if (root is not null)
            {
                root.Margin = new Thickness(0);
                root.HorizontalAlignment = HorizontalAlignment.Stretch;
                root.VerticalAlignment = VerticalAlignment.Center;

                // The approved icon is already part of the exact crop. Hide the old live
                // dot/platform icon only in STALKER so it cannot be drawn twice.
                foreach (var icon in root.Children.OfType<FrameworkElement>().Where(x => x is Image or Ellipse))
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
            foreach (var (border, state) in _cardStates)
            {
                if (border.Child is Grid wrapper && ReferenceEquals(wrapper.Tag, WrapperMarker))
                {
                    if (state.Child is UIElement original)
                        wrapper.Children.Remove(original);
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

            foreach (var (button, state) in _buttonStates)
            {
                button.Background = state.Background;
                button.BorderBrush = state.BorderBrush;
                button.BorderThickness = state.BorderThickness;
                button.Padding = state.Padding;
                button.Margin = state.Margin;
                button.HorizontalContentAlignment = state.HorizontalContentAlignment;
                button.VerticalContentAlignment = state.VerticalContentAlignment;
            }

            foreach (var (element, state) in _elementStates)
            {
                element.Margin = state.Margin;
                element.HorizontalAlignment = state.HorizontalAlignment;
                element.VerticalAlignment = state.VerticalAlignment;
                element.Opacity = state.Opacity;
                element.Visibility = state.Visibility;
                element.IsHitTestVisible = state.IsHitTestVisible;
            }

            foreach (var (text, state) in _textStates)
            {
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

        private static T? FindAncestor<T>(DependencyObject current, DependencyObject? stopAt = null)
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
                border.Child, border.Background, border.BorderBrush, border.BorderThickness,
                border.CornerRadius, border.Padding, border.Margin, border.Width, border.Height,
                border.MinWidth, border.MinHeight, border.HorizontalAlignment, border.VerticalAlignment);
        }

        private void CaptureButton(Button button)
        {
            if (_buttonStates.ContainsKey(button)) return;
            _buttonStates[button] = new ButtonState(
                button.Background, button.BorderBrush, button.BorderThickness, button.Padding,
                button.Margin, button.HorizontalContentAlignment, button.VerticalContentAlignment);
        }

        private void CaptureElement(FrameworkElement element)
        {
            if (_elementStates.ContainsKey(element)) return;
            _elementStates[element] = new ElementState(
                element.Margin, element.HorizontalAlignment, element.VerticalAlignment,
                element.Opacity, element.Visibility, element.IsHitTestVisible);
        }

        private void CaptureText(TextBlock text)
        {
            if (_textStates.ContainsKey(text)) return;
            _textStates[text] = new TextState(
                text.Foreground, text.FontFamily, text.FontWeight, text.FontSize);
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record CardSpec(string Title, Int32Rect Crop, double Width, double RightGap, double TextLeft);

        private sealed record CardState(
            object? Child,
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
