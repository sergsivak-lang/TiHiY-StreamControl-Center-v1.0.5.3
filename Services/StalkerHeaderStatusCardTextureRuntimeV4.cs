using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Applies clean STALKER textured cards to OBS / multistream / Twitch / YouTube.
/// No painted buttons, icons, captions or mock status values are present in the
/// background texture. All visible content is the application's real live UI.
/// </summary>
internal static class StalkerHeaderStatusCardTextureV4Bootstrap
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
            _ = StalkerHeaderStatusCardTextureV4Runtime.Attach(window);
    }
}

internal static class StalkerHeaderStatusCardTextureV4Runtime
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
        private static readonly CardSpec[] Specs =
        {
            new("OBS AUDIO",   142, 8),
            new("MULTISTREAM", 150, 8),
            new("TWITCH",      142, 8),
            new("YOUTUBE",     142, 0)
        };

        private readonly MainWindow _window;
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

                foreach (var element in StalkerApprovedAssets.FindDescendants<FrameworkElement>(card))
                {
                    CaptureElement(element);
                    if (element is TextBlock text) CaptureText(text);
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
            card.Padding = new Thickness(8, 3, 8, 3);
            card.Background = StalkerApprovedAssets.NewTiledBrush(
                "panel-fill-dark.png", 210, 43, 0.98);
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(117, 79, 31));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(2);
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.VerticalAlignment = VerticalAlignment.Center;

            var row = FindStatusRow();
            if (row is not null && FindAncestor<Button>(card, row) is { } button)
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
                button.BorderThickness = new Thickness(0);
                button.Padding = new Thickness(0);
                button.Margin = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
            }

            if (card.Child is StackPanel root)
            {
                root.Margin = new Thickness(0);
                root.HorizontalAlignment = HorizontalAlignment.Stretch;
                root.VerticalAlignment = VerticalAlignment.Center;

                foreach (var icon in root.Children.OfType<FrameworkElement>()
                             .Where(x => x is Image || x is Ellipse))
                {
                    icon.Visibility = Visibility.Visible;
                    icon.Opacity = 1;
                    icon.IsHitTestVisible = false;

                    if (icon is Image)
                    {
                        icon.Width = 21;
                        icon.Height = 21;
                    }
                    else if (icon is Ellipse)
                    {
                        icon.Width = 11;
                        icon.Height = 11;
                    }
                }
            }

            var title = StalkerApprovedAssets.FindDescendants<TextBlock>(card)
                .FirstOrDefault(x => string.Equals(x.Text, spec.Title, StringComparison.OrdinalIgnoreCase));
            if (title is null) return;

            var textStack = FindAncestor<StackPanel>(title, card);
            if (textStack is not null)
            {
                textStack.Margin = new Thickness(7, 0, 0, 0);
                textStack.HorizontalAlignment = HorizontalAlignment.Left;
                textStack.VerticalAlignment = VerticalAlignment.Center;
            }

            title.Foreground = new SolidColorBrush(Color.FromRgb(229, 219, 193));
            title.FontFamily = new FontFamily("Consolas");
            title.FontWeight = FontWeights.Bold;
            title.FontSize = 9.5;

            foreach (var status in StalkerApprovedAssets.FindDescendants<TextBlock>(card)
                         .Where(x => !ReferenceEquals(x, title) && x.Visibility == Visibility.Visible))
            {
                status.FontFamily = new FontFamily("Consolas");
                status.FontWeight = FontWeights.Bold;
                status.FontSize = 8.5;
            }
        }

        private void RestoreAll()
        {
            foreach (var pair in _cardStates)
            {
                var card = pair.Key;
                var state = pair.Value;
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
                element.Width = state.Width;
                element.Height = state.Height;
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
                element.Width,
                element.Height,
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

        private sealed record CardSpec(string Title, double Width, double RightGap);

        private sealed record CardState(
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
            double Width,
            double Height,
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
