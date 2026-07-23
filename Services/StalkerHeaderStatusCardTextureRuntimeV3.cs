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

internal static class StalkerHeaderStatusCardTextureV3Bootstrap
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
            _ = StalkerHeaderStatusCardTextureV3Runtime.Attach(window);
    }
}

internal static class StalkerHeaderStatusCardTextureV3Runtime
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

        // Correct pixel rectangles from approved-full-reference.png (1672 x 941).
        // The previous implementation used coordinates from another header mock-up,
        // which is why fragments of the emblem/title were stretched across the cards.
        private static readonly CardSpec[] Specs =
        {
            new("OBS AUDIO",   new Int32Rect(154, 91, 136, 43), 136, 8, 40),
            new("MULTISTREAM", new Int32Rect(298, 91, 153, 43), 153, 6, 45),
            new("TWITCH",      new Int32Rect(457, 91, 118, 43), 118, 6, 39),
            new("YOUTUBE",     new Int32Rect(581, 91, 121, 43), 121, 0, 41)
        };

        private readonly MainWindow _window;
        private readonly BitmapSource _reference;
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
            _reference = StalkerApprovedAssets.Load("approved-full-reference.png");
            CaptureTargets();

            _window.Closed += OnClosed;
            App.Services.Theme.ThemeChanged += OnThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
        }

        private void CaptureTargets()
        {
            var row = FindStatusRow();
            if (row is null) return;

            foreach (var spec in Specs)
            {
                var title = StalkerApprovedAssets.FindDescendants<TextBlock>(row)
                    .FirstOrDefault(x => string.Equals(x.Text, spec.Title, StringComparison.OrdinalIgnoreCase));
                if (title is null) continue;

                var card = FindAncestor<Border>(title, row);
                if (card is null) continue;

                _cards[spec.Title] = card;
                CaptureCard(card);

                var button = FindAncestor<Button>(card, row);
                if (button is not null) CaptureButton(button);

                if (card.Child is StackPanel root)
                {
                    CaptureElement(root);
                    foreach (var child in root.Children.OfType<FrameworkElement>())
                    {
                        CaptureElement(child);
                        if (child is TextBlock text) CaptureText(text);
                    }
                }

                foreach (var text in StalkerApprovedAssets.FindDescendants<TextBlock>(card))
                {
                    CaptureElement(text);
                    CaptureText(text);
                }
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
                if (_cards.TryGetValue(spec.Title, out var card))
                    ApplyCard(card, spec);
            }
        }

        private void ApplyCard(Border card, CardSpec spec)
        {
            card.Width = spec.Width;
            card.Height = 43;
            card.MinWidth = 0;
            card.MinHeight = 0;
            card.Margin = new Thickness(0, 0, spec.RightGap, 0);
            card.Padding = new Thickness(0);
            card.Background = CropBrush(spec.Crop);
            card.BorderBrush = Brushes.Transparent;
            card.BorderThickness = new Thickness(0);
            card.CornerRadius = new CornerRadius(0);
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.VerticalAlignment = VerticalAlignment.Center;

            var wrapped = card.Child is Grid grid && ReferenceEquals(grid.Tag, WrapperMarker);
            if (!wrapped && _cardStates.TryGetValue(card, out var state))
            {
                var liveContent = state.Child;
                card.Child = null;

                var wrapper = new Grid
                {
                    Tag = WrapperMarker,
                    ClipToBounds = true,
                    Background = Brushes.Transparent
                };

                // Cover only the painted caption from the mock-up. The exact rusty frame
                // and platform icon stay visible; live program captions are placed above.
                wrapper.Children.Add(new Border
                {
                    Margin = new Thickness(spec.TextLeft - 4, 2, 2, 2),
                    CornerRadius = new CornerRadius(1),
                    Background = StalkerApprovedAssets.NewTiledBrush(
                        "panel-fill-dark.png", 180, 39, 1.0),
                    IsHitTestVisible = false
                });

                if (liveContent is not null)
                    wrapper.Children.Add(liveContent);

                card.Child = wrapper;
            }

            var row = FindStatusRow();
            if (row is not null && FindAncestor<Button>(card, row) is { } button)
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
                button.BorderThickness = new Thickness(0);
                button.Padding = new Thickness(0);
                button.Margin = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.VerticalContentAlignment = VerticalAlignment.Center;
            }

            var root = _cardStates.TryGetValue(card, out var captured)
                ? captured.Child as StackPanel
                : null;

            if (root is not null)
            {
                root.Margin = new Thickness(0);
                root.HorizontalAlignment = HorizontalAlignment.Stretch;
                root.VerticalAlignment = VerticalAlignment.Center;

                foreach (var icon in root.Children.OfType<FrameworkElement>()
                             .Where(x => x is Image || x is Ellipse))
                {
                    icon.Visibility = Visibility.Collapsed;
                    icon.IsHitTestVisible = false;
                }
            }

            var title = StalkerApprovedAssets.FindDescendants<TextBlock>(card)
                .FirstOrDefault(x => string.Equals(x.Text, spec.Title, StringComparison.OrdinalIgnoreCase));
            if (title is null) return;

            var textStack = FindAncestor<StackPanel>(title, card);
            if (textStack is not null)
            {
                textStack.Margin = new Thickness(spec.TextLeft, 0, 2, 0);
                textStack.HorizontalAlignment = HorizontalAlignment.Left;
                textStack.VerticalAlignment = VerticalAlignment.Center;
            }

            title.Foreground = new SolidColorBrush(Color.FromRgb(224, 213, 187));
            title.FontFamily = new FontFamily("Consolas");
            title.FontWeight = FontWeights.Bold;
            title.FontSize = 9.5;

            var status = StalkerApprovedAssets.FindDescendants<TextBlock>(card)
                .FirstOrDefault(x => !ReferenceEquals(x, title) && x.Visibility == Visibility.Visible);
            if (status is not null)
            {
                status.FontFamily = new FontFamily("Consolas");
                status.FontWeight = FontWeights.Bold;
                status.FontSize = 8.5;
            }
        }

        private ImageBrush CropBrush(Int32Rect crop)
        {
            var bitmap = new CroppedBitmap(_reference, crop);
            bitmap.Freeze();

            var brush = new ImageBrush(bitmap)
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
                var card = pair.Key;
                var state = pair.Value;

                if (card.Child is Grid wrapper && ReferenceEquals(wrapper.Tag, WrapperMarker))
                {
                    if (state.Child is not null)
                        wrapper.Children.Remove(state.Child);
                    card.Child = state.Child;
                }

                card.Background = state.Background;
                card.BorderBrush = state.BorderBrush;
                card.BorderThickness = state.BorderThickness;
                card.CornerRadius = state.CornerRadius;
                card.Padding = state.Padding;
                card.Margin = state.Margin;
                card.Width = state.Width;
                card.Height = state.Height;
                card.MinWidth = state.MinWidth;
                card.MinHeight = state.MinHeight;
                card.HorizontalAlignment = state.HorizontalAlignment;
                card.VerticalAlignment = state.VerticalAlignment;
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

        private void CaptureCard(Border card)
        {
            if (_cardStates.ContainsKey(card)) return;
            _cardStates[card] = new CardState(
                card.Child,
                card.Background,
                card.BorderBrush,
                card.BorderThickness,
                card.CornerRadius,
                card.Padding,
                card.Margin,
                card.Width,
                card.Height,
                card.MinWidth,
                card.MinHeight,
                card.HorizontalAlignment,
                card.VerticalAlignment);
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

        private void OnClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= OnClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;
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
