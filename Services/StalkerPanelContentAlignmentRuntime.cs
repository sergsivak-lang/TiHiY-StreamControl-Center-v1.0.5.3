using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Aligns live content with the actual painted inner windows of the approved
/// STALKER panel textures. The shell images are stretched with the panel, so a
/// fixed top margin cannot stay aligned. Instead the header row is recalculated
/// from the exact vertical proportion of each source texture while the original
/// 38-DIP live action row remains anchored at the bottom.
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
        private static readonly PanelSpec[] Specs =
        {
            // donations-shell.png: 836 x 298; inner list begins at y = 50.
            new(
                "DonationsBlockPanel",
                50d / 298d,
                new Thickness(28, 0, 26, 0),
                new Thickness(18, 12, 16, 12)),

            // notifications-shell.png: 836 x 208; inner list begins at y = 48.
            new(
                "NotificationsBlockPanel",
                48d / 208d,
                new Thickness(28, 0, 26, 0),
                new Thickness(18, 11, 16, 10)),

            // mixer-shell.png: 816 x 208; channel area begins at y = 46.
            new(
                "MixerBlockPanel",
                46d / 208d,
                new Thickness(28, 0, 26, 0),
                new Thickness(18, 11, 16, 10))
        };

        private readonly MainWindow _window;
        private readonly Dictionary<ContentControl, PanelState> _states = new();
        private readonly HashSet<ContentControl> _sizeHandlers = new();
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
                RestoreUkraineGeometry();

            _lastStalker = stalker;
        }

        private void ApplyStalkerGeometry()
        {
            foreach (var spec in Specs)
            {
                var block = FindNamed<ContentControl>(spec.Name);
                if (block?.Content is not Grid root || root.RowDefinitions.Count < 3)
                    continue;

                Capture(block, root, spec);
                AttachSizeHandler(block);

                // Remove the fixed vertical padding previously added around the live
                // grid. The grid now shares the same vertical coordinate system as
                // the stretched texture, while the approved horizontal frame inset
                // is preserved.
                block.Padding = spec.StalkerPadding;
                block.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                block.VerticalContentAlignment = VerticalAlignment.Stretch;
                block.ClipToBounds = true;

                root.Margin = new Thickness(0);
                root.HorizontalAlignment = HorizontalAlignment.Stretch;
                root.VerticalAlignment = VerticalAlignment.Stretch;
                root.ClipToBounds = true;

                var content = FindRowChild(root, 1);
                if (content is not null)
                {
                    content.Margin = new Thickness(0);
                    content.HorizontalAlignment = HorizontalAlignment.Stretch;
                    content.VerticalAlignment = VerticalAlignment.Stretch;
                    content.ClipToBounds = true;
                }

                UpdateHeaderHeight(block, root, spec);
                Invalidate(block, root, content);
            }
        }

        private void AttachSizeHandler(ContentControl block)
        {
            if (!_sizeHandlers.Add(block))
                return;

            block.SizeChanged += OnPanelSizeChanged;
        }

        private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_disposed || !_lastStalker || sender is not ContentControl block)
                return;

            var spec = Specs.FirstOrDefault(item => string.Equals(item.Name, block.Name, StringComparison.Ordinal));
            if (spec is null || block.Content is not Grid root || root.RowDefinitions.Count < 3)
                return;

            UpdateHeaderHeight(block, root, spec);
        }

        private static void UpdateHeaderHeight(ContentControl block, Grid root, PanelSpec spec)
        {
            var panelHeight = block.ActualHeight;
            if (panelHeight <= 1)
                return;

            // Match the shell's painted boundary at every window size and UI zoom.
            // Keep the stock footer row at 38 DIP because its real WPF button must
            // sit inside the small painted button frame rather than fill the entire
            // decorative footer band.
            var headerHeight = Math.Round(panelHeight * spec.HeaderRatio, 2);
            var maxHeader = Math.Max(42, panelHeight - 76);
            headerHeight = Math.Clamp(headerHeight, 42, maxHeader);

            root.RowDefinitions[0].Height = new GridLength(headerHeight, GridUnitType.Pixel);
            root.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions[2].Height = new GridLength(38, GridUnitType.Pixel);
        }

        private void RestoreUkraineGeometry()
        {
            foreach (var pair in _states)
            {
                var block = pair.Key;
                var state = pair.Value;
                if (block.Content is not Grid root)
                    continue;

                block.Padding = state.Spec.UkrainePadding;
                block.HorizontalContentAlignment = state.HorizontalContentAlignment;
                block.VerticalContentAlignment = state.VerticalContentAlignment;
                block.ClipToBounds = state.BlockClipToBounds;

                root.Margin = state.RootMargin;
                root.HorizontalAlignment = state.RootHorizontalAlignment;
                root.VerticalAlignment = state.RootVerticalAlignment;
                root.ClipToBounds = state.RootClipToBounds;

                RestoreRows(root, state.Rows);

                var content = FindRowChild(root, 1);
                if (content is not null && state.Content is not null)
                {
                    content.Margin = state.Content.Margin;
                    content.HorizontalAlignment = state.Content.HorizontalAlignment;
                    content.VerticalAlignment = state.Content.VerticalAlignment;
                    content.ClipToBounds = state.Content.ClipToBounds;
                }

                Invalidate(block, root, content);
            }
        }

        private void Capture(ContentControl block, Grid root, PanelSpec spec)
        {
            if (_states.ContainsKey(block))
                return;

            var content = FindRowChild(root, 1);
            _states[block] = new PanelState(
                spec,
                root.RowDefinitions.Select(row =>
                    new RowState(row.Height, row.MinHeight, row.MaxHeight)).ToArray(),
                block.HorizontalContentAlignment,
                block.VerticalContentAlignment,
                block.ClipToBounds,
                root.Margin,
                root.HorizontalAlignment,
                root.VerticalAlignment,
                root.ClipToBounds,
                content is null
                    ? null
                    : new ElementState(
                        content.Margin,
                        content.HorizontalAlignment,
                        content.VerticalAlignment,
                        content.ClipToBounds));
        }

        private static FrameworkElement? FindRowChild(Grid root, int row) =>
            root.Children.OfType<FrameworkElement>()
                .FirstOrDefault(child => Grid.GetRow(child) == row);

        private static void RestoreRows(Grid root, IReadOnlyList<RowState> rows)
        {
            root.RowDefinitions.Clear();
            foreach (var row in rows)
            {
                root.RowDefinitions.Add(new RowDefinition
                {
                    Height = row.Height,
                    MinHeight = row.MinHeight,
                    MaxHeight = row.MaxHeight
                });
            }
        }

        private static void Invalidate(ContentControl block, Grid root, FrameworkElement? content)
        {
            content?.InvalidateMeasure();
            content?.InvalidateArrange();
            content?.InvalidateVisual();
            root.InvalidateMeasure();
            root.InvalidateArrange();
            root.InvalidateVisual();
            block.InvalidateMeasure();
            block.InvalidateArrange();
            block.InvalidateVisual();
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

            foreach (var block in _sizeHandlers)
                block.SizeChanged -= OnPanelSizeChanged;
            _sizeHandlers.Clear();

            RestoreUkraineGeometry();
        }

        private sealed record PanelSpec(
            string Name,
            double HeaderRatio,
            Thickness StalkerPadding,
            Thickness UkrainePadding);

        private sealed record PanelState(
            PanelSpec Spec,
            IReadOnlyList<RowState> Rows,
            HorizontalAlignment HorizontalContentAlignment,
            VerticalAlignment VerticalContentAlignment,
            bool BlockClipToBounds,
            Thickness RootMargin,
            HorizontalAlignment RootHorizontalAlignment,
            VerticalAlignment RootVerticalAlignment,
            bool RootClipToBounds,
            ElementState? Content);

        private sealed record ElementState(
            Thickness Margin,
            HorizontalAlignment HorizontalAlignment,
            VerticalAlignment VerticalAlignment,
            bool ClipToBounds);

        private sealed record RowState(
            GridLength Height,
            double MinHeight,
            double MaxHeight);
    }
}
