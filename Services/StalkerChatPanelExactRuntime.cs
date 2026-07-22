using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Final STALKER owner for the multichat block. The approved painted shell is used
/// only as a background; every counter, list, input and send button remains live.
/// A fixed 512 x 254 design surface is scaled with the block so the active chat area
/// always matches the window painted in the texture at every application zoom level.
/// </summary>
internal static class StalkerChatPanelExactBootstrap
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
            _ = StalkerChatPanelExactRuntime.Attach(window);
    }
}

internal static class StalkerChatPanelExactRuntime
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
        private readonly MainWindow _window;
        private ContentControl? _chat;
        private Grid? _root;
        private Grid? _header;
        private Border? _body;
        private Grid? _footer;
        private StackPanel? _titleStack;
        private StackPanel? _counterStack;
        private Viewbox? _viewbox;
        private TextBox? _chatInput;
        private Button? _twitchButton;
        private Button? _youtubeButton;
        private Button? _bothButton;
        private Button? _sendButton;
        private Border? _viewerPanel;

        private readonly Dictionary<FrameworkElement, ElementState> _elementStates = new();
        private readonly Dictionary<Border, BorderState> _borderStates = new();
        private readonly Dictionary<Control, ControlState> _controlStates = new();
        private readonly Dictionary<Panel, Brush?> _panelBackgrounds = new();
        private List<RowState>? _originalRows;
        private List<ColumnState>? _originalFooterColumns;
        private ContentState? _chatState;
        private bool _wrapped;
        private bool _lastStalker;
        private bool _queued;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _window.ContentRendered += OnContentRendered;
            _window.Closed += OnClosed;
            App.Services.Theme.ThemeChanged += OnThemeChanged;
            QueueApply();
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            _window.ContentRendered -= OnContentRendered;
            QueueApply();
        }

        private void OnThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply()
        {
            if (_disposed || _queued) return;
            _queued = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _queued = false;
                ApplyNow();
            }), DispatcherPriority.SystemIdle);
        }

        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            if (!EnsureTargets()) return;

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
                ApplyStalker();
            else if (_lastStalker || _wrapped)
                RestoreOriginal();

            _lastStalker = stalker;
        }

        private bool EnsureTargets()
        {
            if (_chat is not null && _root is not null) return true;

            _chat = StalkerApprovedAssets.FindDescendants<ContentControl>(_window)
                .FirstOrDefault(x => string.Equals(x.Name, "ChatBlockPanel", StringComparison.Ordinal));
            if (_chat?.Content is not Grid root) return false;

            _root = root;
            _header = root.Children.OfType<Grid>().FirstOrDefault(x => Grid.GetRow(x) == 0);
            _body = root.Children.OfType<Border>().FirstOrDefault(x => Grid.GetRow(x) == 1);
            _footer = root.Children.OfType<Grid>().FirstOrDefault(x => Grid.GetRow(x) == 2);
            if (_header is null || _body is null || _footer is null) return false;

            _titleStack = _header.Children.OfType<StackPanel>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 0);
            _counterStack = _header.Children.OfType<StackPanel>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 1);

            _chatInput = FindNamed<TextBox>("ChatInput");
            _twitchButton = FindNamed<Button>("SendTwitchButton");
            _youtubeButton = FindNamed<Button>("SendYouTubeButton");
            _bothButton = FindNamed<Button>("SendBothButton");
            _sendButton = _footer.Children.OfType<Button>()
                .FirstOrDefault(x => !ReferenceEquals(x, _twitchButton) &&
                                     !ReferenceEquals(x, _youtubeButton) &&
                                     !ReferenceEquals(x, _bothButton));

            CaptureInitialState();
            return true;
        }

        private void CaptureInitialState()
        {
            if (_chat is null || _root is null || _header is null || _body is null || _footer is null)
                return;

            _chatState ??= new ContentState(
                _chat.Content,
                _chat.Background,
                _chat.BorderBrush,
                _chat.BorderThickness,
                _chat.Padding,
                _chat.HorizontalContentAlignment,
                _chat.VerticalContentAlignment,
                _chat.ClipToBounds);

            _originalRows ??= _root.RowDefinitions
                .Select(x => new RowState(x.Height, x.MinHeight, x.MaxHeight))
                .ToList();
            _originalFooterColumns ??= _footer.ColumnDefinitions
                .Select(x => new ColumnState(x.Width, x.MinWidth, x.MaxWidth))
                .ToList();

            SaveElement(_root);
            SaveElement(_header);
            SaveElement(_body);
            SaveElement(_footer);
            if (_titleStack is not null) SaveElement(_titleStack);
            if (_counterStack is not null) SaveElement(_counterStack);

            SavePanel(_root);
            SavePanel(_header);
            SavePanel(_footer);
            SaveBorder(_body);

            SaveControl(_chatInput);
            SaveControl(_twitchButton);
            SaveControl(_youtubeButton);
            SaveControl(_bothButton);
            SaveControl(_sendButton);

            foreach (var border in CounterBorders()) SaveBorder(border);
        }

        private void ApplyStalker()
        {
            if (_chat is null || _root is null || _header is null || _body is null || _footer is null)
                return;

            if (!_wrapped)
            {
                _chat.Content = null;
                _viewbox = new Viewbox
                {
                    Stretch = Stretch.Fill,
                    StretchDirection = StretchDirection.Both,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    SnapsToDevicePixels = true,
                    Child = _root
                };
                _chat.Content = _viewbox;
                _wrapped = true;
            }

            _chat.Background = StalkerApprovedAssets.NewStretchBrush(
                "chat-multichat-panel-exact.jpg", 1.0);
            _chat.BorderBrush = Brushes.Transparent;
            _chat.BorderThickness = new Thickness(0);
            _chat.Padding = new Thickness(0);
            _chat.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _chat.VerticalContentAlignment = VerticalAlignment.Stretch;
            _chat.ClipToBounds = true;

            _root.Width = 512;
            _root.Height = 254;
            _root.MinWidth = 0;
            _root.MinHeight = 0;
            _root.Margin = new Thickness(0);
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _root.VerticalAlignment = VerticalAlignment.Stretch;
            _root.Background = Brushes.Transparent;

            _root.RowDefinitions.Clear();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(185) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11) });

            Grid.SetRow(_header, 0);
            _header.Margin = new Thickness(15, 0, 21, 0);
            _header.Background = Brushes.Transparent;
            if (_titleStack is not null)
                _titleStack.Visibility = Visibility.Collapsed;
            if (_counterStack is not null)
            {
                _counterStack.Margin = new Thickness(0, 3, 0, 3);
                _counterStack.HorizontalAlignment = HorizontalAlignment.Right;
                _counterStack.VerticalAlignment = VerticalAlignment.Center;
            }

            ConfigureTopCounters();

            Grid.SetRow(_body, 1);
            _body.Margin = new Thickness(15, 0, 21, 0);
            _body.Padding = new Thickness(0);
            _body.Background = Brushes.Transparent;
            _body.BorderBrush = Brushes.Transparent;
            _body.BorderThickness = new Thickness(0);
            _body.CornerRadius = new CornerRadius(0);
            _body.HorizontalAlignment = HorizontalAlignment.Stretch;
            _body.VerticalAlignment = VerticalAlignment.Stretch;

            Grid.SetRow(_footer, 3);
            _footer.Margin = new Thickness(15, 0, 21, 0);
            _footer.Background = Brushes.Transparent;
            ConfigureFooter();
            ConfigureViewerPanel();

            _chat.InvalidateMeasure();
            _chat.InvalidateArrange();
            _chat.InvalidateVisual();
        }

        private void ConfigureTopCounters()
        {
            var borders = CounterBorders().ToArray();
            for (var index = 0; index < borders.Length; index++)
            {
                var border = borders[index];
                SaveElement(border);
                SaveBorder(border);
                border.Width = index == 0 ? 31 : 63;
                border.Height = 18;
                border.MinWidth = 0;
                border.MinHeight = 0;
                border.Margin = new Thickness(2, 1, 0, 1);
                border.Padding = new Thickness(2, 0, 2, 0);
                border.Background = new SolidColorBrush(Color.FromArgb(220, 5, 13, 18));
                border.BorderThickness = new Thickness(1);
                border.CornerRadius = new CornerRadius(2);
            }

            foreach (var text in new[]
                     {
                         FindNamed<TextBlock>("TwitchViewerText"),
                         FindNamed<TextBlock>("YouTubeViewerText"),
                         FindNamed<TextBlock>("YouTubeLikesText")
                     }.Where(x => x is not null))
            {
                SaveElement(text!);
                text!.FontSize = 8;
                text.Margin = new Thickness(2, 0, 2, 0);
                text.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        private void ConfigureFooter()
        {
            if (_footer is null) return;

            _footer.ColumnDefinitions.Clear();
            _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(166) });
            _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            PlaceFooterControl(_chatInput, 0, new Thickness(0, 2, 4, 2));
            PlaceFooterControl(_twitchButton, 1, new Thickness(1, 2, 2, 2));
            PlaceFooterControl(_youtubeButton, 2, new Thickness(1, 2, 2, 2));
            PlaceFooterControl(_bothButton, 3, new Thickness(1, 2, 2, 2));
            PlaceFooterControl(_sendButton, 5, new Thickness(1, 2, 0, 2));
        }

        private void PlaceFooterControl(Control? control, int column, Thickness margin)
        {
            if (control is null) return;
            SaveElement(control);
            SaveControl(control);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, 1);
            control.Width = double.NaN;
            control.Height = 20;
            control.MinWidth = 0;
            control.MinHeight = 0;
            control.Margin = margin;
            control.Padding = control is TextBox
                ? new Thickness(4, 0, 4, 0)
                : new Thickness(1, 0, 1, 0);
            control.Background = Brushes.Transparent;
            control.BorderBrush = Brushes.Transparent;
            control.BorderThickness = new Thickness(0);
            control.HorizontalContentAlignment = HorizontalAlignment.Center;
            control.VerticalContentAlignment = VerticalAlignment.Center;
        }

        private void ConfigureViewerPanel()
        {
            if (_chat is null || _body is null) return;

            _viewerPanel ??= StalkerApprovedAssets.FindDescendants<Border>(_chat)
                .FirstOrDefault(border => StalkerApprovedAssets.FindDescendants<TextBlock>(border)
                    .Any(text => text.Text?.Contains("ГЛЯДАЧІ", StringComparison.OrdinalIgnoreCase) == true));

            if (_viewerPanel is null || ReferenceEquals(_viewerPanel, _body)) return;

            SaveElement(_viewerPanel);
            SaveBorder(_viewerPanel);
            _viewerPanel.Width = 153;
            _viewerPanel.Height = 110;
            _viewerPanel.MinWidth = 0;
            _viewerPanel.MinHeight = 0;
            _viewerPanel.Margin = new Thickness(0);
            _viewerPanel.HorizontalAlignment = HorizontalAlignment.Left;
            _viewerPanel.VerticalAlignment = VerticalAlignment.Bottom;
            _viewerPanel.Background = Brushes.Transparent;
            _viewerPanel.BorderBrush = Brushes.Transparent;
            _viewerPanel.BorderThickness = new Thickness(0);
            _viewerPanel.CornerRadius = new CornerRadius(0);
        }

        private IEnumerable<Border> CounterBorders()
        {
            if (_header is null) yield break;

            var found = new HashSet<Border>();
            foreach (var name in new[] { "TwitchViewerText", "YouTubeViewerText", "YouTubeLikesText" })
            {
                var text = FindNamed<TextBlock>(name);
                var border = text is null ? null : FindAncestor<Border>(text, _header);
                if (border is not null && found.Add(border)) yield return border;
            }
        }

        private void RestoreOriginal()
        {
            if (_chat is null || _root is null) return;

            if (_wrapped)
            {
                if (_viewbox is not null) _viewbox.Child = null;
                _chat.Content = _root;
                _viewbox = null;
                _wrapped = false;
            }

            if (_chatState is not null)
            {
                _chat.Background = _chatState.Background;
                _chat.BorderBrush = _chatState.BorderBrush;
                _chat.BorderThickness = _chatState.BorderThickness;
                _chat.Padding = _chatState.Padding;
                _chat.HorizontalContentAlignment = _chatState.HorizontalContentAlignment;
                _chat.VerticalContentAlignment = _chatState.VerticalContentAlignment;
                _chat.ClipToBounds = _chatState.ClipToBounds;
            }

            if (_originalRows is not null)
            {
                _root.RowDefinitions.Clear();
                foreach (var state in _originalRows)
                    _root.RowDefinitions.Add(new RowDefinition
                    {
                        Height = state.Height,
                        MinHeight = state.MinHeight,
                        MaxHeight = state.MaxHeight
                    });
            }

            if (_footer is not null && _originalFooterColumns is not null)
            {
                _footer.ColumnDefinitions.Clear();
                foreach (var state in _originalFooterColumns)
                    _footer.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = state.Width,
                        MinWidth = state.MinWidth,
                        MaxWidth = state.MaxWidth
                    });
            }

            foreach (var pair in _panelBackgrounds)
                pair.Key.Background = pair.Value;

            foreach (var pair in _borderStates)
            {
                var border = pair.Key;
                var state = pair.Value;
                border.Background = state.Background;
                border.BorderBrush = state.BorderBrush;
                border.BorderThickness = state.BorderThickness;
                border.CornerRadius = state.CornerRadius;
                border.Padding = state.Padding;
            }

            foreach (var pair in _controlStates)
            {
                var control = pair.Key;
                var state = pair.Value;
                control.Background = state.Background;
                control.BorderBrush = state.BorderBrush;
                control.BorderThickness = state.BorderThickness;
                control.Padding = state.Padding;
                control.HorizontalContentAlignment = state.HorizontalContentAlignment;
                control.VerticalContentAlignment = state.VerticalContentAlignment;
            }

            foreach (var pair in _elementStates)
            {
                var element = pair.Key;
                var state = pair.Value;
                element.Width = state.Width;
                element.Height = state.Height;
                element.MinWidth = state.MinWidth;
                element.MinHeight = state.MinHeight;
                element.Margin = state.Margin;
                element.HorizontalAlignment = state.HorizontalAlignment;
                element.VerticalAlignment = state.VerticalAlignment;
                element.Visibility = state.Visibility;
                Grid.SetRow(element, state.Row);
                Grid.SetColumn(element, state.Column);
                Grid.SetRowSpan(element, state.RowSpan);
                Grid.SetColumnSpan(element, state.ColumnSpan);
            }

            _chat.InvalidateMeasure();
            _chat.InvalidateArrange();
            _chat.InvalidateVisual();
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            _chat is null
                ? null
                : StalkerApprovedAssets.FindDescendants<T>(_chat)
                    .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

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

        private void SaveElement(FrameworkElement? element)
        {
            if (element is null || _elementStates.ContainsKey(element)) return;
            _elementStates[element] = new ElementState(
                element.Width,
                element.Height,
                element.MinWidth,
                element.MinHeight,
                element.Margin,
                element.HorizontalAlignment,
                element.VerticalAlignment,
                element.Visibility,
                Grid.GetRow(element),
                Grid.GetColumn(element),
                Grid.GetRowSpan(element),
                Grid.GetColumnSpan(element));
        }

        private void SavePanel(Panel? panel)
        {
            if (panel is null || _panelBackgrounds.ContainsKey(panel)) return;
            _panelBackgrounds[panel] = panel.Background;
        }

        private void SaveBorder(Border? border)
        {
            if (border is null || _borderStates.ContainsKey(border)) return;
            _borderStates[border] = new BorderState(
                border.Background,
                border.BorderBrush,
                border.BorderThickness,
                border.CornerRadius,
                border.Padding);
        }

        private void SaveControl(Control? control)
        {
            if (control is null || _controlStates.ContainsKey(control)) return;
            _controlStates[control] = new ControlState(
                control.Background,
                control.BorderBrush,
                control.BorderThickness,
                control.Padding,
                control.HorizontalContentAlignment,
                control.VerticalContentAlignment);
        }

        private void OnClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.ContentRendered -= OnContentRendered;
            _window.Closed -= OnClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;
            RestoreOriginal();
        }

        private sealed record ContentState(
            object? Content,
            Brush? Background,
            Brush? BorderBrush,
            Thickness BorderThickness,
            Thickness Padding,
            HorizontalAlignment HorizontalContentAlignment,
            VerticalAlignment VerticalContentAlignment,
            bool ClipToBounds);

        private sealed record ElementState(
            double Width,
            double Height,
            double MinWidth,
            double MinHeight,
            Thickness Margin,
            HorizontalAlignment HorizontalAlignment,
            VerticalAlignment VerticalAlignment,
            Visibility Visibility,
            int Row,
            int Column,
            int RowSpan,
            int ColumnSpan);

        private sealed record BorderState(
            Brush? Background,
            Brush? BorderBrush,
            Thickness BorderThickness,
            CornerRadius CornerRadius,
            Thickness Padding);

        private sealed record ControlState(
            Brush? Background,
            Brush? BorderBrush,
            Thickness BorderThickness,
            Thickness Padding,
            HorizontalAlignment HorizontalContentAlignment,
            VerticalAlignment VerticalContentAlignment);

        private sealed record RowState(GridLength Height, double MinHeight, double MaxHeight);
        private sealed record ColumnState(GridLength Width, double MinWidth, double MaxWidth);
    }
}
