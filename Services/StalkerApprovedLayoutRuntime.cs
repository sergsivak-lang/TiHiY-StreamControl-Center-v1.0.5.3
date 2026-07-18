using System.Runtime.CompilerServices;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Applies the approved 1672x941 geometry to the real grid-based final window.
/// The final program uses the actual DesignSurface, DashboardBlocksGrid and FooterBlocksGrid hierarchy.
/// </summary>
internal static class StalkerApprovedLayoutBootstrap
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
            _ = StalkerApprovedGridLayoutRuntime.Attach(window);
    }
}

public static class StalkerApprovedGridLayoutRuntime
{
    private const double DesignWidth = 1672;
    private const double DesignHeight = 941;
    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    public static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private readonly MainWindow _window;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<FrameworkElement, ElementState> _elements = new();
        private readonly Dictionary<RowDefinition, GridLength> _rows = new();
        private readonly Dictionary<ColumnDefinition, GridLength> _columns = new();
        private WindowStateSnapshot? _windowState;
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(240)
            };
            _timer.Tick += TimerTick;
            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            _timer.Start();
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Loaded);
        }

        private void TimerTick(object? sender, EventArgs e)
        {
            if (_disposed || !_window.IsLoaded) return;
            if (StalkerApprovedAssets.IsStalkerTheme() || _lastStalker) ApplyNow();
        }

        private void ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.Render);

        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                CaptureWindow();
                ApplyWindow();
                ApplyExactRows();
                ApplyExactColumns();
                ApplyExactMargins();
            }
            else if (_lastStalker)
            {
                RestoreElements();
                RestoreRows();
                RestoreColumns();
                RestoreWindow();
            }
            _lastStalker = stalker;
        }

        private void CaptureWindow()
        {
            _windowState ??= new WindowStateSnapshot(
                _window.Width, _window.Height, _window.MinWidth, _window.MinHeight,
                _window.MaxWidth, _window.MaxHeight, _window.Left, _window.Top,
                _window.SizeToContent, _window.WindowState);
        }

        private void ApplyWindow()
        {
            var work = SystemParameters.WorkArea;
            _window.WindowState = WindowState.Normal;
            _window.SizeToContent = SizeToContent.Manual;
            _window.MinWidth = 1200;
            _window.MinHeight = 700;
            _window.MaxWidth = Math.Max(DesignWidth, work.Width);
            _window.MaxHeight = Math.Max(DesignHeight, work.Height);
            _window.Width = DesignWidth;
            _window.Height = DesignHeight;
            _window.Left = work.Left + Math.Max(0, (work.Width - DesignWidth) / 2);
            _window.Top = work.Top + Math.Max(0, (work.Height - DesignHeight) / 2);
        }

        private void ApplyExactRows()
        {
            var design = FindNamed<Grid>("DesignSurface");
            var dashboard = FindNamed<Grid>("DashboardBlocksGrid");
            if (design is null || dashboard is null) return;

            CaptureElement(design);
            CaptureElement(dashboard);
            design.Margin = new Thickness(5, 0, 5, 14);
            dashboard.Margin = new Thickness(0);

            // Global approved Y coordinates:
            // header/status 0..145, top 145..443, middle 449..657,
            // footer 664..927, outer bottom frame 927..941.
            SetRow(design, 0, 88);
            SetRow(design, 1, 57);
            SetRow(design, 2, 512);
            SetRow(design, 3, 7);
            SetRow(design, 4, 263);

            SetRow(dashboard, 0, 298);
            SetRow(dashboard, 1, 6);
            SetRow(dashboard, 2, 208);
        }

        private void ApplyExactColumns()
        {
            var top = FindNamed<Grid>("TopBlocksGrid");
            var bottom = FindNamed<Grid>("BottomBlocksGrid");
            var footer = FindNamed<Grid>("FooterBlocksGrid");
            if (top is not null)
            {
                SetColumn(top, 0, 816);
                SetColumn(top, 1, 10);
                SetColumn(top, 2, 836);
            }
            if (bottom is not null)
            {
                SetColumn(bottom, 0, 816);
                SetColumn(bottom, 1, 10);
                SetColumn(bottom, 2, 836);
            }
            if (footer is not null)
            {
                SetColumn(footer, 0, 583);
                SetColumn(footer, 1, 2);
                SetColumn(footer, 2, 445);
                SetColumn(footer, 3, 0);
                SetColumn(footer, 4, 632);
            }
        }

        private void ApplyExactMargins()
        {
            foreach (var name in new[]
                     {
                         "ChatBlockPanel", "DonationsBlockPanel", "MixerBlockPanel",
                         "NotificationsBlockPanel", "SystemStatusBlockPanel"
                     })
            {
                var element = FindNamed<FrameworkElement>(name);
                if (element is null) continue;
                CaptureElement(element);
                element.Margin = new Thickness(0);
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
                element.VerticalAlignment = VerticalAlignment.Stretch;
            }

            var monitor = FindNamed<FrameworkElement>("SystemMonitorPanel");
            if (monitor is not null)
            {
                CaptureElement(monitor);
                monitor.Margin = new Thickness(-5, 0, 0, 0);
                monitor.HorizontalAlignment = HorizontalAlignment.Stretch;
                monitor.VerticalAlignment = VerticalAlignment.Stretch;
            }

            var footer = FindNamed<Grid>("FooterBlocksGrid");
            var center = footer?.Children.OfType<ContentControl>().FirstOrDefault(x => Grid.GetColumn(x) == 2);
            if (center is not null)
            {
                CaptureElement(center);
                center.Margin = new Thickness(0);
                center.HorizontalAlignment = HorizontalAlignment.Stretch;
                center.VerticalAlignment = VerticalAlignment.Stretch;
                center.Tag = "UkraineCenterBlock";
            }

            var design = FindNamed<Grid>("DesignSurface");
            var statusRow = design?.Children.OfType<Grid>().FirstOrDefault(x => Grid.GetRow(x) == 1);
            if (statusRow is not null)
            {
                CaptureElement(statusRow);
                statusRow.Margin = new Thickness(145, 0, 0, 5);
            }
        }

        private void SetRow(Grid grid, int index, double value)
        {
            if (index < 0 || index >= grid.RowDefinitions.Count) return;
            var row = grid.RowDefinitions[index];
            if (!_rows.ContainsKey(row)) _rows[row] = row.Height;
            row.MinHeight = 0;
            row.MaxHeight = double.PositiveInfinity;
            row.Height = new GridLength(value, GridUnitType.Pixel);
        }

        private void SetColumn(Grid grid, int index, double value)
        {
            if (index < 0 || index >= grid.ColumnDefinitions.Count) return;
            var column = grid.ColumnDefinitions[index];
            if (!_columns.ContainsKey(column)) _columns[column] = column.Width;
            column.MinWidth = 0;
            column.MaxWidth = double.PositiveInfinity;
            column.Width = new GridLength(value, GridUnitType.Pixel);
        }

        private void CaptureElement(FrameworkElement element)
        {
            if (_elements.ContainsKey(element)) return;
            _elements[element] = new ElementState(
                element.Margin, element.HorizontalAlignment, element.VerticalAlignment,
                element.Width, element.Height, element.MinWidth, element.MinHeight);
        }

        private void RestoreElements()
        {
            foreach (var (element, state) in _elements)
            {
                element.Margin = state.Margin;
                element.HorizontalAlignment = state.HorizontalAlignment;
                element.VerticalAlignment = state.VerticalAlignment;
                element.Width = state.Width;
                element.Height = state.Height;
                element.MinWidth = state.MinWidth;
                element.MinHeight = state.MinHeight;
            }
        }

        private void RestoreRows()
        {
            foreach (var (row, height) in _rows) row.Height = height;
        }

        private void RestoreColumns()
        {
            foreach (var (column, width) in _columns) column.Width = width;
        }

        private void RestoreWindow()
        {
            if (_windowState is null) return;
            var s = _windowState;
            _window.WindowState = WindowState.Normal;
            _window.SizeToContent = s.SizeToContent;
            _window.Width = s.Width;
            _window.Height = s.Height;
            _window.MinWidth = s.MinWidth;
            _window.MinHeight = s.MinHeight;
            _window.MaxWidth = s.MaxWidth;
            _window.MaxHeight = s.MaxHeight;
            _window.Left = s.Left;
            _window.Top = s.Top;
            _window.WindowState = s.WindowState;
        }

        private T? FindNamed<T>(string name) where T : FrameworkElement =>
            StalkerApprovedAssets.FindDescendants<T>(_window).FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.Ordinal));

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= TimerTick;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private sealed record ElementState(
            Thickness Margin,
            HorizontalAlignment HorizontalAlignment,
            VerticalAlignment VerticalAlignment,
            double Width,
            double Height,
            double MinWidth,
            double MinHeight);

        private sealed record WindowStateSnapshot(
            double Width,
            double Height,
            double MinWidth,
            double MinHeight,
            double MaxWidth,
            double MaxHeight,
            double Left,
            double Top,
            SizeToContent SizeToContent,
            WindowState WindowState);
    }
}
