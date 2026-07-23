using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Aligns the live content of the STALKER Donations, Notifications and OBS Mixer
/// panels with the painted inner windows of their approved shell textures.
/// The shell title bands are taller than the stock Ukraine layout, so the row-1
/// content must start lower while the bottom action strip stays anchored in place.
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
        if (Controllers.TryGetValue(window, out var existing))
            return existing;

        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private static readonly IReadOnlyDictionary<string, Thickness> ContentMargins =
            new Dictionary<string, Thickness>(StringComparer.Ordinal)
            {
                // The donation shell has the deepest painted title band.
                ["DonationsBlockPanel"] = new Thickness(0, 22, 0, 5),

                // Notification and mixer shells use the same lower inner-window line.
                ["NotificationsBlockPanel"] = new Thickness(0, 20, 0, 5),
                ["MixerBlockPanel"] = new Thickness(0, 20, 0, 5)
            };

        private readonly MainWindow _window;
        private readonly Dictionary<FrameworkElement, ElementState> _states = new();
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
            if (_disposed || _queued)
                return;

            _queued = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _queued = false;
                ApplyNow();
            }), DispatcherPriority.SystemIdle);
        }

        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded)
                return;

            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
                ApplyStalkerGeometry();
            else if (_lastStalker)
                RestoreOriginalGeometry();

            _lastStalker = stalker;
        }

        private void ApplyStalkerGeometry()
        {
            foreach (var pair in ContentMargins)
            {
                var block = FindNamed<ContentControl>(pair.Key);
                if (block?.Content is not Grid root)
                    continue;

                var content = root.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(child => Grid.GetRow(child) == 1);
                if (content is null)
                    continue;

                Save(content);
                content.Margin = pair.Value;
                content.VerticalAlignment = VerticalAlignment.Stretch;
                content.HorizontalAlignment = HorizontalAlignment.Stretch;
                content.ClipToBounds = true;

                // Keep the action strip at the painted bottom frame. Only the active
                // row is moved down; no overlay, Viewbox or timer is introduced.
                content.InvalidateMeasure();
                content.InvalidateArrange();
                content.InvalidateVisual();
                block.InvalidateMeasure();
                block.InvalidateArrange();
                block.InvalidateVisual();
            }
        }

        private void RestoreOriginalGeometry()
        {
            foreach (var pair in _states)
            {
                var element = pair.Key;
                var state = pair.Value;
                element.Margin = state.Margin;
                element.VerticalAlignment = state.VerticalAlignment;
                element.HorizontalAlignment = state.HorizontalAlignment;
                element.ClipToBounds = state.ClipToBounds;
                element.InvalidateMeasure();
                element.InvalidateArrange();
                element.InvalidateVisual();
            }
        }

        private void Save(FrameworkElement element)
        {
            if (_states.ContainsKey(element))
                return;

            _states[element] = new ElementState(
                element.Margin,
                element.VerticalAlignment,
                element.HorizontalAlignment,
                element.ClipToBounds);
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            StalkerApprovedAssets.FindDescendants<T>(_window)
                .FirstOrDefault(element => string.Equals(element.Name, name, StringComparison.Ordinal));

        private void OnClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _window.ContentRendered -= OnContentRendered;
            _window.Closed -= OnClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;
            RestoreOriginalGeometry();
        }

        private sealed record ElementState(
            Thickness Margin,
            VerticalAlignment VerticalAlignment,
            HorizontalAlignment HorizontalAlignment,
            bool ClipToBounds);
    }
}
