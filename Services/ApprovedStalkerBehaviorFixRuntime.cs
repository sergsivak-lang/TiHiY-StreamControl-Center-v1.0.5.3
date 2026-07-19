using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Approved post-test corrections. The pass is deliberately additive and
/// theme-local: it does not move dashboard blocks or change other themes.
/// </summary>
internal static class ApprovedStalkerBehaviorFixBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainLoaded));
        EventManager.RegisterClassHandler(typeof(SettingsWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSettingsLoaded));
    }

    private static void OnMainLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            _ = ApprovedStalkerBehaviorFixRuntime.Attach(window);
    }

    private static void OnSettingsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsWindow window)
            ApprovedStalkerBehaviorFixRuntime.FixSettingsCaption(window);
    }
}

internal static class ApprovedStalkerBehaviorFixRuntime
{
    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    internal static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    internal static void FixSettingsCaption(SettingsWindow window)
    {
        if (!StalkerApprovedAssets.IsStalkerTheme()) return;

        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            // Keep one real caption-button group. Any additional generated group
            // is hidden; painted texture controls are covered by the live header.
            var candidates = StalkerApprovedAssets.FindDescendants<Button>(window)
                .Where(b => b.Content?.ToString() is "—" or "−" or "□" or "×")
                .OrderByDescending(b => b.TranslatePoint(new Point(), window).X)
                .ToList();

            foreach (var duplicate in candidates.Skip(3))
            {
                duplicate.Visibility = Visibility.Collapsed;
                duplicate.IsHitTestVisible = false;
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private sealed class Controller : IDisposable
    {
        private readonly MainWindow _window;
        private ListBox? _chat;
        private INotifyCollectionChanged? _chatCollection;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _window.Closed += WindowClosed;
            _window.ContentRendered += ContentRendered;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            QueueApply();
        }

        private void ContentRendered(object? sender, EventArgs e) => QueueApply();
        private void ThemeChanged(object? sender, EventArgs e) => QueueApply();

        private void QueueApply() => _window.Dispatcher.BeginInvoke(
            new Action(Apply), DispatcherPriority.ApplicationIdle);

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;

            ApplyChatBehavior();
            StabilizeChatComposer();

            if (StalkerApprovedAssets.IsStalkerTheme())
            {
                ApplyApprovedCenterArtwork();
                ShiftHeaderControlsInsideWindow();
            }
        }

        private void ApplyChatBehavior()
        {
            _chat ??= FindNamed<ListBox>("MainChatList");
            if (_chat is null) return;

            VirtualizingPanel.SetIsVirtualizing(_chat, true);
            VirtualizingPanel.SetVirtualizationMode(_chat, VirtualizationMode.Standard);
            ScrollViewer.SetCanContentScroll(_chat, true);

            var view = CollectionViewSource.GetDefaultView(_chat.ItemsSource);
            if (view is not null && !view.SortDescriptions.Any(x => x.PropertyName == "Time"))
            {
                using (view.DeferRefresh())
                {
                    view.SortDescriptions.Clear();
                    view.SortDescriptions.Add(new SortDescription("Time", ListSortDirection.Descending));
                }
            }

            if (_chatCollection is null && _chat.ItemsSource is INotifyCollectionChanged collection)
            {
                _chatCollection = collection;
                _chatCollection.CollectionChanged += ChatCollectionChanged;
            }

            ScrollNewestToTop();
        }

        private void ChatCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
                _window.Dispatcher.BeginInvoke(new Action(ScrollNewestToTop), DispatcherPriority.Background);
        }

        private void ScrollNewestToTop()
        {
            if (_chat is null || _chat.Items.Count == 0) return;
            _chat.ScrollIntoView(_chat.Items[0]);
            if (FindVisualChild<ScrollViewer>(_chat) is { } viewer)
                viewer.ScrollToTop();
        }

        private void StabilizeChatComposer()
        {
            var input = FindNamed<TextBox>("ChatInput");
            if (input is null) return;

            input.Height = 38;
            input.MinHeight = 38;
            input.VerticalAlignment = VerticalAlignment.Center;

            if (VisualTreeHelper.GetParent(input) is not Grid composer) return;
            composer.Height = 44;
            composer.MinHeight = 44;
            composer.VerticalAlignment = VerticalAlignment.Bottom;

            foreach (var button in composer.Children.OfType<Button>())
            {
                button.Height = 38;
                button.MinHeight = 38;
                button.VerticalAlignment = VerticalAlignment.Center;
                button.Margin = new Thickness(3, 0, 3, 0);
            }
        }

        private void ApplyApprovedCenterArtwork()
        {
            var footer = FindNamed<Grid>("FooterBlocksGrid");
            var center = FindNamed<ContentControl>("ModulesBlockPanel")
                         ?? footer?.Children.OfType<ContentControl>()
                             .FirstOrDefault(x => Grid.GetColumn(x) == 2);
            if (center is null) return;

            var root = new Grid { ClipToBounds = true };
            root.Children.Add(new Image
            {
                Source = LoadThemeImage("Assets/Themes/StalkerExact/zone-banner.png"),
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            });
            root.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(10, 0, 0, 0), Color.FromArgb(180, 0, 0, 0), 90),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 72,
                IsHitTestVisible = false
            });
            root.Children.Add(new TextBlock
            {
                Text = "S.T.A.L.K.E.R.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 8, 13),
                FontFamily = new FontFamily("Bahnschrift SemiCondensed, Impact, Consolas"),
                FontSize = 31,
                FontWeight = FontWeights.Black,
                CharacterSpacing = 180,
                Foreground = new SolidColorBrush(Color.FromRgb(221, 211, 181)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 3, ShadowDepth = 2, Opacity = .95
                },
                IsHitTestVisible = false
            });

            center.Content = root;
            center.Background = Brushes.Transparent;
            center.Padding = new Thickness(0);
            center.BorderThickness = new Thickness(0);
        }

        private void ShiftHeaderControlsInsideWindow()
        {
            var design = FindNamed<Grid>("DesignSurface");
            if (design is null) return;

            foreach (var panel in StalkerApprovedAssets.FindDescendants<StackPanel>(design))
            {
                var texts = panel.Children.OfType<Button>()
                    .Select(b => b.Content?.ToString() ?? string.Empty).ToArray();
                if (texts.Contains("МАКЕТ: ЗАКРІПЛЕНО") || texts.Contains("×"))
                    panel.Margin = new Thickness(panel.Margin.Left, panel.Margin.Top,
                        Math.Max(panel.Margin.Right, 26), panel.Margin.Bottom);
            }
        }

        private static BitmapImage LoadThemeImage(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/{path}", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            StalkerApprovedAssets.FindDescendants<T>(_window)
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) return typed;
                if (FindVisualChild<T>(child) is { } nested) return nested;
            }
            return null;
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_chatCollection is not null) _chatCollection.CollectionChanged -= ChatCollectionChanged;
            _window.Closed -= WindowClosed;
            _window.ContentRendered -= ContentRendered;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }
    }
}
