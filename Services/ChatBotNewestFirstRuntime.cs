using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Keeps the dedicated chat-bot window consistent with the main multichat:
/// newest messages stay at the top and autoscroll follows the newest item.
/// </summary>
internal static class ChatBotNewestFirstBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(ChatBotWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ChatBotWindow window)
            _ = ChatBotNewestFirstRuntime.Attach(window);
    }
}

internal static class ChatBotNewestFirstRuntime
{
    private static readonly ConditionalWeakTable<ChatBotWindow, Controller> Controllers = new();

    internal static IDisposable Attach(ChatBotWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private readonly ChatBotWindow _window;
        private ListBox? _list;
        private INotifyCollectionChanged? _collection;
        private bool _scrollQueued;
        private bool _disposed;

        internal Controller(ChatBotWindow window)
        {
            _window = window;
            _window.Closed += WindowClosed;
            _window.ContentRendered += ContentRendered;
            QueueApply();
        }

        private void ContentRendered(object? sender, EventArgs e) => QueueApply();

        private void QueueApply() => _window.Dispatcher.BeginInvoke(
            new Action(Apply), DispatcherPriority.ApplicationIdle);

        private void Apply()
        {
            if (_disposed || !_window.IsLoaded) return;

            _list ??= FindNamed<ListBox>("ChatMessagesList");
            if (_list is null) return;

            // Do not change WPF virtualization attached properties here. At this point
            // the ItemsControl may already have completed Measure, and changing
            // VirtualizationMode then throws repeatedly. Virtualization remains owned
            // by the original XAML/control template.
            var view = CollectionViewSource.GetDefaultView(_list.ItemsSource);
            if (view is not null &&
                (view.SortDescriptions.Count != 1 ||
                 view.SortDescriptions[0].PropertyName != "Time" ||
                 view.SortDescriptions[0].Direction != ListSortDirection.Descending))
            {
                using (view.DeferRefresh())
                {
                    view.SortDescriptions.Clear();
                    view.SortDescriptions.Add(new SortDescription("Time", ListSortDirection.Descending));
                }
            }

            if (_collection is null && _list.ItemsSource is INotifyCollectionChanged collection)
            {
                _collection = collection;
                _collection.CollectionChanged += CollectionChanged;
            }

            QueueScrollNewestToTop();
        }

        private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace)
                QueueScrollNewestToTop();
        }

        private void QueueScrollNewestToTop()
        {
            if (_disposed || _scrollQueued) return;
            _scrollQueued = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _scrollQueued = false;
                ScrollNewestToTop();
            }), DispatcherPriority.ContextIdle);
        }

        private void ScrollNewestToTop()
        {
            if (_disposed || _list is null || _list.Items.Count == 0) return;
            _list.ScrollIntoView(_list.Items[0]);
            FindVisualChild<ScrollViewer>(_list)?.ScrollToTop();
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
            if (_collection is not null) _collection.CollectionChanged -= CollectionChanged;
            _window.Closed -= WindowClosed;
            _window.ContentRendered -= ContentRendered;
        }
    }
}
