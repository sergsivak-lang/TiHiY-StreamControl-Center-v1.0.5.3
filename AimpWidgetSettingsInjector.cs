using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Services;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter;

internal static class AimpWidgetSettingsInjector
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(OverlaySettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not OverlaySettingsWindow window || Equals(window.Tag, "AIMP_WIDGET_CONTROLS")) return;
        window.Tag = "AIMP_WIDGET_CONTROLS";
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => Attach(window)));
    }

    private static void Attach(OverlaySettingsWindow window)
    {
        if (window.FindName("NowPlayingUrlBox") is not TextBox urlBox) return;
        if (urlBox.Parent is not StackPanel parent) return;

        var settingsFolder = App.Services.SettingsService.Folder;
        var preferences = AimpWidgetPreferences.Load(settingsFolder);
        var updatingUrl = false;

        var opacityValue = new TextBlock
        {
            Width = 48,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindBrush(window, "Text"),
            Text = $"{preferences.BackgroundOpacityPercent}%"
        };
        var opacity = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 10,
            TickFrequency = 5,
            IsSnapToTickEnabled = false,
            Value = preferences.BackgroundOpacityPercent,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var heightValue = new TextBlock
        {
            Width = 58,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindBrush(window, "Text"),
            Text = $"{preferences.HeightPixels}px"
        };
        var height = new Slider
        {
            Minimum = 72,
            Maximum = 140,
            SmallChange = 1,
            LargeChange = 8,
            TickFrequency = 4,
            IsSnapToTickEnabled = false,
            Value = preferences.HeightPixels,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var panel = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        var opacityLabel = new TextBlock
        {
            Text = "Прозорість фону",
            Foreground = FindBrush(window, "Muted"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var heightLabel = new TextBlock
        {
            Text = "Висота віджета",
            Foreground = FindBrush(window, "Muted"),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetRow(opacityLabel, 0);
        Grid.SetRow(opacity, 0);
        Grid.SetColumn(opacity, 1);
        Grid.SetRow(opacityValue, 0);
        Grid.SetColumn(opacityValue, 2);
        Grid.SetRow(heightLabel, 1);
        Grid.SetRow(height, 1);
        Grid.SetColumn(height, 1);
        Grid.SetRow(heightValue, 1);
        Grid.SetColumn(heightValue, 2);
        panel.Children.Add(opacityLabel);
        panel.Children.Add(opacity);
        panel.Children.Add(opacityValue);
        panel.Children.Add(heightLabel);
        panel.Children.Add(height);
        panel.Children.Add(heightValue);

        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 6, 29, 48)),
            BorderBrush = FindBrush(window, "Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 6, 0, 4),
            Child = panel
        };

        var index = parent.Children.IndexOf(urlBox);
        parent.Children.Insert(Math.Min(parent.Children.Count, index + 2), container);

        void ApplyUrl()
        {
            if (updatingUrl) return;
            updatingUrl = true;
            try
            {
                preferences.BackgroundOpacityPercent = (int)Math.Round(opacity.Value);
                preferences.HeightPixels = (int)Math.Round(height.Value);
                preferences.Save(settingsFolder);
                opacityValue.Text = $"{preferences.BackgroundOpacityPercent}%";
                heightValue.Text = $"{preferences.HeightPixels}px";
                urlBox.Text = preferences.ApplyToUrl(urlBox.Text);
            }
            finally { updatingUrl = false; }
        }

        opacity.ValueChanged += (_, _) => ApplyUrl();
        height.ValueChanged += (_, _) => ApplyUrl();
        urlBox.TextChanged += (_, _) =>
        {
            if (!updatingUrl && !urlBox.Text.Contains("opacity=", StringComparison.OrdinalIgnoreCase))
                ApplyUrl();
        };

        ApplyUrl();
    }

    private static Brush FindBrush(FrameworkElement owner, string key) =>
        owner.TryFindResource(key) as Brush ?? Brushes.Transparent;
}
