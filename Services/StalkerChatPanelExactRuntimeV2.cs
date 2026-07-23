using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Single final owner of the STALKER multichat panel. The JPG is only a clean shell:
/// no painted viewer overlay and no painted footer controls. All counters, chat history,
/// input and buttons are real WPF controls aligned to the 512 x 254 reference geometry.
/// </summary>
internal static class StalkerChatPanelExactV2Bootstrap
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
            _ = StalkerChatPanelExactV2Runtime.Attach(window);
    }
}

internal static class StalkerChatPanelExactV2Runtime
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
        private readonly Dictionary<FrameworkElement, ElementState> _elements = new();
        private readonly Dictionary<Border, BorderState> _borders = new();
        private readonly Dictionary<Control, ControlState> _controls = new();
        private readonly Dictionary<Panel, Brush?> _panelBackgrounds = new();
        private readonly List<Button> _utilityButtons = new();

        private ContentControl? _chat;
        private Grid? _root;
        private Grid? _header;
        private Border? _body;
        private Grid? _footer;
        private StackPanel? _titleStack;
        private StackPanel? _counterStack;
        private Border? _legacyViewerOverlay;
        private TextBox? _chatInput;
        private Button? _twitchButton;
        private Button? _youtubeButton;
        private Button? _bothButton;
        private TextBlock? _emptyState;
        private ListBox? _chatList;

        private ContentState? _chatState;
        private List<RowState>? _rows;
        private List<ColumnState>? _rootColumns;
        private List<ColumnState>? _footerColumns;
        private bool _queued;
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;

            // Capture pristine XAML values immediately during Loaded, before older
            // deferred theme code can write STALKER values. This guarantees a clean
            // return to the Ukraine theme even when the app starts in STALKER mode.
            EnsureTargets();

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
            if (_disposed || !_window.IsLoaded || !EnsureTargets()) return;

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
                ApplyStalker();
            else if (_lastStalker)
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

            _titleStack = _header.Children.OfType<StackPanel>().FirstOrDefault(x => Grid.GetColumn(x) == 0);
            _counterStack = _header.Children.OfType<StackPanel>().FirstOrDefault(x => Grid.GetColumn(x) == 1);
            _chatInput = FindNamed<TextBox>("ChatInput");
            _twitchButton = FindNamed<Button>("SendTwitchButton");
            _youtubeButton = FindNamed<Button>("SendYouTubeButton");
            _bothButton = FindNamed<Button>("SendBothButton");
            _emptyState = FindNamed<TextBlock>("ChatEmptyStateText");
            _chatList = FindNamed<ListBox>("MainChatList");

            _utilityButtons.Clear();
            _utilityButtons.AddRange(_footer.Children.OfType<Button>().Where(x =>
                !ReferenceEquals(x, _twitchButton) &&
                !ReferenceEquals(x, _youtubeButton) &&
                !ReferenceEquals(x, _bothButton)));

            _legacyViewerOverlay = StalkerApprovedAssets.FindDescendants<Border>(_chat)
                .FirstOrDefault(border => !ReferenceEquals(border, _body) &&
                    StalkerApprovedAssets.FindDescendants<TextBlock>(border)
                        .Any(text => text.Text?.Contains("ГЛЯДАЧІ", StringComparison.OrdinalIgnoreCase) == true));

            CaptureOriginal();
            return true;
        }

        private void CaptureOriginal()
        {
            if (_chat is null || _root is null || _header is null || _body is null || _footer is null) return;

            _chatState ??= new ContentState(
                _chat.Background, _chat.BorderBrush, _chat.BorderThickness, _chat.Padding,
                _chat.HorizontalContentAlignment, _chat.VerticalContentAlignment, _chat.ClipToBounds);

            _rows ??= _root.RowDefinitions.Select(x => new RowState(x.Height, x.MinHeight, x.MaxHeight)).ToList();
            _rootColumns ??= _root.ColumnDefinitions.Select(x => new ColumnState(x.Width, x.MinWidth, x.MaxWidth)).ToList();
            _footerColumns ??= _footer.ColumnDefinitions.Select(x => new ColumnState(x.Width, x.MinWidth, x.MaxWidth)).ToList();

            SaveElement(_root);
            SaveElement(_header);
            SaveElement(_body);
            SaveElement(_footer);
            SaveElement(_titleStack);
            SaveElement(_counterStack);
            SaveElement(_legacyViewerOverlay);
            SaveElement(_chatInput);
            SaveElement(_twitchButton);
            SaveElement(_youtubeButton);
            SaveElement(_bothButton);
            SaveElement(_emptyState);
            SaveElement(_chatList);
            foreach (var button in _utilityButtons) SaveElement(button);

            SavePanel(_root);
            SavePanel(_header);
            SavePanel(_footer);
            SaveBorder(_body);
            SaveBorder(_legacyViewerOverlay);
            SaveControl(_chatInput);
            SaveControl(_twitchButton);
            SaveControl(_youtubeButton);
            SaveControl(_bothButton);
            SaveControl(_chatList);
            foreach (var button in _utilityButtons) SaveControl(button);

            foreach (var border in CounterBorders())
            {
                SaveElement(border);
                SaveBorder(border);
            }
        }

        private void ApplyStalker()
        {
            if (_chat is null || _root is null || _header is null || _body is null || _footer is null) return;

            _chat.Background = LoadTextureBrush();
            _chat.BorderBrush = Brushes.Transparent;
            _chat.BorderThickness = new Thickness(0);
            _chat.Padding = new Thickness(0);
            _chat.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _chat.VerticalContentAlignment = VerticalAlignment.Stretch;
            _chat.ClipToBounds = true;

            _root.Width = double.NaN;
            _root.Height = double.NaN;
            _root.MinWidth = 0;
            _root.MinHeight = 0;
            _root.Margin = new Thickness(0);
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _root.VerticalAlignment = VerticalAlignment.Stretch;
            _root.Background = Brushes.Transparent;

            // 512 x 254 reference: 15 px left frame, 482 px live width, 15 px right frame.
            _root.ColumnDefinitions.Clear();
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(482, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });

            // Title/counters 30 px, chat history 188 px, live footer 36 px.
            _root.RowDefinitions.Clear();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(188, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36, GridUnitType.Star) });

            PlaceInInterior(_header, 0);
            PlaceInInterior(_body, 1);
            PlaceInInterior(_footer, 2);

            _header.Margin = new Thickness(0);
            _header.Background = Brushes.Transparent;
            if (_titleStack is not null) _titleStack.Visibility = Visibility.Collapsed;
            ConfigureCounters();

            _body.Margin = new Thickness(0);
            _body.Padding = new Thickness(5, 3, 5, 4);
            _body.Background = Brushes.Transparent;
            _body.BorderBrush = Brushes.Transparent;
            _body.BorderThickness = new Thickness(0);
            _body.CornerRadius = new CornerRadius(0);
            _body.HorizontalAlignment = HorizontalAlignment.Stretch;
            _body.VerticalAlignment = VerticalAlignment.Stretch;

            if (_chatList is not null)
            {
                _chatList.Background = Brushes.Transparent;
                _chatList.BorderBrush = Brushes.Transparent;
                _chatList.BorderThickness = new Thickness(0);
                _chatList.Margin = new Thickness(0);
            }

            if (_emptyState is not null)
            {
                _emptyState.Margin = new Thickness(0);
                _emptyState.HorizontalAlignment = HorizontalAlignment.Center;
                _emptyState.VerticalAlignment = VerticalAlignment.Center;
            }

            // This compact viewer panel belongs only to the external game overlay.
            // It must never remain over the desktop chat history.
            if (_legacyViewerOverlay is not null)
                _legacyViewerOverlay.Visibility = Visibility.Collapsed;

            _footer.Margin = new Thickness(0);
            _footer.Background = Brushes.Transparent;
            ConfigureFooter();

            _chat.InvalidateMeasure();
            _chat.InvalidateArrange();
            _chat.InvalidateVisual();
        }

        private static void PlaceInInterior(FrameworkElement element, int row)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, 1);
            Grid.SetColumnSpan(element, 1);
        }

        private void ConfigureCounters()
        {
            if (_counterStack is not null)
            {
                _counterStack.Margin = new Thickness(0, 3, 2, 3);
                _counterStack.HorizontalAlignment = HorizontalAlignment.Right;
                _counterStack.VerticalAlignment = VerticalAlignment.Stretch;
            }

            var borders = CounterBorders().ToArray();
            for (var index = 0; index < borders.Length; index++)
            {
                var border = borders[index];
                border.Width = index == 0 ? 34 : 68;
                border.Height = double.NaN;
                border.MinWidth = 0;
                border.MinHeight = 0;
                border.Margin = new Thickness(2, 0, 0, 0);
                border.Padding = new Thickness(3, 0, 3, 0);
                border.Background = Brushes.Transparent;
                border.BorderBrush = Brushes.Transparent;
                border.BorderThickness = new Thickness(0);
                border.CornerRadius = new CornerRadius(0);
            }

            foreach (var text in new[]
                     {
                         FindNamed<TextBlock>("TwitchViewerText"),
                         FindNamed<TextBlock>("YouTubeViewerText"),
                         FindNamed<TextBlock>("YouTubeLikesText")
                     }.Where(x => x is not null))
            {
                SaveElement(text!);
                text!.FontSize = 9;
                text.Margin = new Thickness(2, 0, 2, 0);
                text.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        private void ConfigureFooter()
        {
            if (_footer is null) return;

            // Live controls replace all painted footer controls. Widths sum to 482.
            _footer.ColumnDefinitions.Clear();
            foreach (var width in new[] { 170d, 6d, 90d, 6d, 90d, 6d, 70d, 6d, 18d, 20d })
                _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });

            PlaceFooter(_chatInput, 0, true);
            PlaceFooter(_twitchButton, 2, false);
            PlaceFooter(_youtubeButton, 4, false);
            PlaceFooter(_bothButton, 6, false);
            if (_utilityButtons.Count > 0) PlaceFooter(_utilityButtons[0], 8, false);

            for (var i = 1; i < _utilityButtons.Count; i++)
                _utilityButtons[i].Visibility = Visibility.Collapsed;
        }

        private static void PlaceFooter(Control? control, int column, bool input)
        {
            if (control is null) return;

            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, 1);
            control.Width = double.NaN;
            control.Height = double.NaN;
            control.MinWidth = 0;
            control.MinHeight = 0;
            control.Margin = new Thickness(0, 3, 0, 9);
            control.Padding = input ? new Thickness(7, 0, 7, 0) : new Thickness(2, 0, 2, 0);
            control.Background = new SolidColorBrush(Color.FromArgb(235, 5, 13, 17));
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(150, 105, 38));
            control.BorderThickness = new Thickness(1);
            control.Foreground = new SolidColorBrush(Color.FromRgb(226, 218, 194));
            control.FontFamily = new FontFamily("Consolas");
            control.FontSize = input ? 10.5 : 9.5;
            control.HorizontalContentAlignment = input ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            control.VerticalContentAlignment = VerticalAlignment.Center;
            control.Visibility = Visibility.Visible;
        }

        private Brush LoadTextureBrush()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory,
                    "Assets", "Themes", "StalkerApproved", "chat-multichat-panel-exact.jpg");

                if (!File.Exists(path))
                    return StalkerApprovedAssets.NewStretchBrush("chat-shell.png", 1.0);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
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
            catch
            {
                return StalkerApprovedAssets.NewStretchBrush("chat-shell.png", 1.0);
            }
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

            RestoreRows(_root, _rows);
            RestoreColumns(_root, _rootColumns);
            if (_footer is not null) RestoreColumns(_footer, _footerColumns);

            foreach (var pair in _panelBackgrounds) pair.Key.Background = pair.Value;
            foreach (var pair in _borders)
            {
                var b = pair.Key;
                var s = pair.Value;
                b.Background = s.Background;
                b.BorderBrush = s.BorderBrush;
                b.BorderThickness = s.BorderThickness;
                b.CornerRadius = s.CornerRadius;
                b.Padding = s.Padding;
            }
            foreach (var pair in _controls)
            {
                var c = pair.Key;
                var s = pair.Value;
                c.Background = s.Background;
                c.BorderBrush = s.BorderBrush;
                c.BorderThickness = s.BorderThickness;
                c.Padding = s.Padding;
                c.Foreground = s.Foreground;
                c.FontFamily = s.FontFamily;
                c.FontSize = s.FontSize;
                c.HorizontalContentAlignment = s.HorizontalContentAlignment;
                c.VerticalContentAlignment = s.VerticalContentAlignment;
            }
            foreach (var pair in _elements)
            {
                var e = pair.Key;
                var s = pair.Value;
                e.Width = s.Width;
                e.Height = s.Height;
                e.MinWidth = s.MinWidth;
                e.MinHeight = s.MinHeight;
                e.Margin = s.Margin;
                e.HorizontalAlignment = s.HorizontalAlignment;
                e.VerticalAlignment = s.VerticalAlignment;
                e.Visibility = s.Visibility;
                Grid.SetRow(e, s.Row);
                Grid.SetColumn(e, s.Column);
                Grid.SetRowSpan(e, s.RowSpan);
                Grid.SetColumnSpan(e, s.ColumnSpan);
            }

            _chat.InvalidateMeasure();
            _chat.InvalidateArrange();
            _chat.InvalidateVisual();
        }

        private static void RestoreRows(Grid grid, List<RowState>? states)
        {
            if (states is null) return;
            grid.RowDefinitions.Clear();
            foreach (var s in states)
                grid.RowDefinitions.Add(new RowDefinition { Height = s.Height, MinHeight = s.MinHeight, MaxHeight = s.MaxHeight });
        }

        private static void RestoreColumns(Grid grid, List<ColumnState>? states)
        {
            if (states is null) return;
            grid.ColumnDefinitions.Clear();
            foreach (var s in states)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = s.Width, MinWidth = s.MinWidth, MaxWidth = s.MaxWidth });
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            _chat is null ? null : StalkerApprovedAssets.FindDescendants<T>(_chat)
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

        private static T? FindAncestor<T>(DependencyObject current, DependencyObject stopAt) where T : DependencyObject
        {
            var parent = StalkerApprovedAssets.GetParent(current);
            while (parent is not null && !ReferenceEquals(parent, stopAt))
            {
                if (parent is T match) return match;
                parent = StalkerApprovedAssets.GetParent(parent);
            }
            return null;
        }

        private void SaveElement(FrameworkElement? e)
        {
            if (e is null || _elements.ContainsKey(e)) return;
            _elements[e] = new ElementState(e.Width, e.Height, e.MinWidth, e.MinHeight, e.Margin,
                e.HorizontalAlignment, e.VerticalAlignment, e.Visibility,
                Grid.GetRow(e), Grid.GetColumn(e), Grid.GetRowSpan(e), Grid.GetColumnSpan(e));
        }

        private void SavePanel(Panel? p)
        {
            if (p is null || _panelBackgrounds.ContainsKey(p)) return;
            _panelBackgrounds[p] = p.Background;
        }

        private void SaveBorder(Border? b)
        {
            if (b is null || _borders.ContainsKey(b)) return;
            _borders[b] = new BorderState(b.Background, b.BorderBrush, b.BorderThickness, b.CornerRadius, b.Padding);
        }

        private void SaveControl(Control? c)
        {
            if (c is null || _controls.ContainsKey(c)) return;
            _controls[c] = new ControlState(c.Background, c.BorderBrush, c.BorderThickness, c.Padding,
                c.Foreground, c.FontFamily, c.FontSize,
                c.HorizontalContentAlignment, c.VerticalContentAlignment);
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

        private sealed record ContentState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness,
            Thickness Padding, HorizontalAlignment HorizontalContentAlignment,
            VerticalAlignment VerticalContentAlignment, bool ClipToBounds);
        private sealed record ElementState(double Width, double Height, double MinWidth, double MinHeight,
            Thickness Margin, HorizontalAlignment HorizontalAlignment, VerticalAlignment VerticalAlignment,
            Visibility Visibility, int Row, int Column, int RowSpan, int ColumnSpan);
        private sealed record BorderState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness,
            CornerRadius CornerRadius, Thickness Padding);
        private sealed record ControlState(Brush? Background, Brush? BorderBrush, Thickness BorderThickness,
            Thickness Padding, Brush? Foreground, FontFamily FontFamily, double FontSize,
            HorizontalAlignment HorizontalContentAlignment, VerticalAlignment VerticalContentAlignment);
        private sealed record RowState(GridLength Height, double MinHeight, double MaxHeight);
        private sealed record ColumnState(GridLength Width, double MinWidth, double MaxWidth);
    }
}
