using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Keeps the live rows below the painted STALKER title bands. Unlike the previous
/// proportional experiment, these values are stable at every window size: only
/// row 0 grows, row 1 remains flexible and the real bottom action row stays fixed.
/// </summary>
internal static class StalkerPanelContentAlignmentBootstrap
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
            _ = StalkerPanelContentAlignmentRuntime.Attach(window);
    }
}

internal static class StalkerPanelContentAlignmentRuntime
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
        private static readonly IReadOnlyDictionary<string, double> HeaderHeights =
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["DonationsBlockPanel"] = 64,
                ["NotificationsBlockPanel"] = 62,
                ["MixerBlockPanel"] = 62
            };

        private readonly MainWindow _window;
        private readonly Dictionary<Grid, RowState[]> _originalRows = new();
        private bool _queued;
        private bool _lastStalker;
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

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
                ApplyStalker();
            else if (_lastStalker)
                RestoreOriginal();

            _lastStalker = stalker;
        }

        private void ApplyStalker()
        {
            foreach (var pair in HeaderHeights)
            {
                var panel = FindNamed<ContentControl>(pair.Key);
                if (panel?.Content is not Grid root || root.RowDefinitions.Count < 3)
                    continue;

                Capture(root);

                root.RowDefinitions[0].Height = new GridLength(pair.Value, GridUnitType.Pixel);
                root.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                root.RowDefinitions[2].Height = new GridLength(38, GridUnitType.Pixel);

                var active = root.Children.OfType<FrameworkElement>()
                    .FirstOrDefault(child => Grid.GetRow(child) == 1);
                if (active is not null)
                {
                    active.Margin = new Thickness(0);
                    active.HorizontalAlignment = HorizontalAlignment.Stretch;
                    active.VerticalAlignment = VerticalAlignment.Stretch;
                    active.ClipToBounds = true;
                    active.InvalidateMeasure();
                    active.InvalidateArrange();
                }

                root.InvalidateMeasure();
                root.InvalidateArrange();
                panel.InvalidateMeasure();
                panel.InvalidateArrange();
            }
        }

        private void Capture(Grid root)
        {
            if (_originalRows.ContainsKey(root)) return;
            _originalRows[root] = root.RowDefinitions
                .Select(row => new RowState(row.Height, row.MinHeight, row.MaxHeight))
                .ToArray();
        }

        private void RestoreOriginal()
        {
            foreach (var pair in _originalRows)
            {
                var root = pair.Key;
                root.RowDefinitions.Clear();
                foreach (var state in pair.Value)
                {
                    root.RowDefinitions.Add(new RowDefinition
                    {
                        Height = state.Height,
                        MinHeight = state.MinHeight,
                        MaxHeight = state.MaxHeight
                    });
                }
                root.InvalidateMeasure();
                root.InvalidateArrange();
            }
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            StalkerApprovedAssets.FindDescendants<T>(_window)
                .FirstOrDefault(element => string.Equals(element.Name, name, StringComparison.Ordinal));

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

        private sealed record RowState(GridLength Height, double MinHeight, double MaxHeight);
    }
}
