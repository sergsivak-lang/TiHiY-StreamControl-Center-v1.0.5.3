using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Replaces the old functional lower-center ContentControl with one dedicated
/// painted artwork surface. Ukraine and STALKER each get their own resource.
/// The old panel is kept collapsed only to preserve the existing grid geometry.
/// </summary>
internal static class ThemeCenterPaintedArtworkBootstrap
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
            _ = ThemeCenterPaintedArtworkRuntime.Attach(window);
    }
}

internal static class ThemeCenterPaintedArtworkRuntime
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
        private const string UkraineArtworkUri =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/UkraineExact/central-glory.jpg";

        private const string StalkerArtworkUri =
            "pack://application:,,,/TiHiY.StreamControlCenter;component/Assets/Themes/StalkerApproved/center-zone-panel-exact.png";

        private readonly MainWindow _window;
        private readonly BitmapImage _ukraineArtwork;
        private readonly BitmapImage _stalkerArtwork;

        private Grid? _footer;
        private ContentControl? _oldCenterPanel;
        private Visibility _oldCenterVisibility;
        private Border? _artworkHost;
        private Image? _artworkImage;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _ukraineArtwork = LoadArtwork(UkraineArtworkUri);
            _stalkerArtwork = LoadArtwork(StalkerArtworkUri);

            _window.Closed += OnWindowClosed;
            App.Services.Theme.ThemeChanged += OnThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(EnsureAndApply), DispatcherPriority.Loaded);
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            _window.Dispatcher.BeginInvoke(new Action(EnsureAndApply), DispatcherPriority.Render);
        }

        private void EnsureAndApply()
        {
            if (_disposed || !_window.IsLoaded)
                return;

            if (!EnsureArtworkHost())
                return;

            var currentTheme = App.Services.Theme.CurrentTheme;
            if (string.Equals(currentTheme, "Сталкер", StringComparison.OrdinalIgnoreCase))
            {
                _artworkImage!.Source = _stalkerArtwork;
                _artworkHost!.Visibility = Visibility.Visible;
            }
            else if (string.Equals(currentTheme, "Україна", StringComparison.OrdinalIgnoreCase))
            {
                _artworkImage!.Source = _ukraineArtwork;
                _artworkHost!.Visibility = Visibility.Visible;
            }
            else
            {
                _artworkImage!.Source = null;
                _artworkHost!.Visibility = Visibility.Collapsed;
            }

            _oldCenterPanel!.Visibility = Visibility.Collapsed;
            _artworkHost!.InvalidateVisual();
        }

        private bool EnsureArtworkHost()
        {
            if (_artworkHost is not null)
                return true;

            _footer = StalkerApprovedAssets.FindDescendants<Grid>(_window)
                .FirstOrDefault(x => string.Equals(
                    x.Name,
                    "FooterBlocksGrid",
                    StringComparison.Ordinal));

            if (_footer is null)
                return false;

            _oldCenterPanel = _footer.Children
                .OfType<ContentControl>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 2);

            if (_oldCenterPanel is null)
                return false;

            _oldCenterVisibility = _oldCenterPanel.Visibility;
            _oldCenterPanel.Visibility = Visibility.Collapsed;

            _artworkImage = new Image
            {
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };

            _artworkHost = new Border
            {
                Margin = new Thickness(3, 0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ClipToBounds = true,
                Child = _artworkImage,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };

            Grid.SetColumn(_artworkHost, 2);
            Panel.SetZIndex(_artworkHost, 1000);
            _footer.Children.Add(_artworkHost);
            return true;
        }

        private static BitmapImage LoadArtwork(string uri)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _window.Closed -= OnWindowClosed;
            App.Services.Theme.ThemeChanged -= OnThemeChanged;

            if (_footer is not null && _artworkHost is not null)
                _footer.Children.Remove(_artworkHost);

            if (_oldCenterPanel is not null)
                _oldCenterPanel.Visibility = _oldCenterVisibility;
        }
    }
}
