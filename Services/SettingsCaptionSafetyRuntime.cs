using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Prevents the STALKER caption cleanup from hiding ordinary minus buttons
/// inside Settings (for example the UI scale control). Only top-right window
/// caption groups are considered duplicates.
/// </summary>
internal static class SettingsCaptionSafetyBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(SettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsWindow window || !StalkerApprovedAssets.IsStalkerTheme())
            return;

        window.Dispatcher.BeginInvoke(
            new Action(() => Apply(window)),
            DispatcherPriority.ContextIdle);
    }

    private static void Apply(SettingsWindow window)
    {
        var symbolButtons = StalkerApprovedAssets.FindDescendants<Button>(window)
            .Where(button => button.Content?.ToString() is "—" or "−" or "□" or "×")
            .Select(button => new
            {
                Button = button,
                Position = SafePosition(button, window)
            })
            .ToList();

        // Restore normal controls that happen to use the same symbols.
        foreach (var item in symbolButtons.Where(item => !IsCaptionButton(item.Button, item.Position, window)))
        {
            item.Button.Visibility = Visibility.Visible;
            item.Button.IsHitTestVisible = true;
        }

        var captionButtons = symbolButtons
            .Where(item => IsCaptionButton(item.Button, item.Position, window))
            .OrderByDescending(item => item.Position.X)
            .ToList();

        // Keep the right-most real caption triplet and hide only extra triplets.
        var keptButtons = captionButtons.Take(3).ToList();
        foreach (var item in keptButtons)
        {
            item.Button.Visibility = Visibility.Visible;
            item.Button.IsHitTestVisible = true;
        }

        foreach (var item in captionButtons.Skip(3))
        {
            item.Button.Visibility = Visibility.Collapsed;
            item.Button.IsHitTestVisible = false;
        }

        // The approved mock-up leaves a small safety gap at the right edge.
        // Move the surviving caption group left without altering button sizes.
        var captionPanel = keptButtons
            .Select(item => FindParentPanel(item.Button))
            .FirstOrDefault(panel => panel is not null);

        if (captionPanel is not null)
        {
            var margin = captionPanel.Margin;
            captionPanel.Margin = new Thickness(
                margin.Left,
                margin.Top,
                Math.Max(margin.Right, 16),
                margin.Bottom);
        }
    }

    private static Panel? FindParentPanel(DependencyObject child)
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is Panel panel)
                return panel;
        }

        return null;
    }

    private static bool IsCaptionButton(Button button, Point position, Window window)
    {
        var width = double.IsNaN(button.ActualWidth) || button.ActualWidth <= 0
            ? button.Width
            : button.ActualWidth;
        var height = double.IsNaN(button.ActualHeight) || button.ActualHeight <= 0
            ? button.Height
            : button.ActualHeight;

        return position.Y >= -4
               && position.Y <= 72
               && position.X >= Math.Max(0, window.ActualWidth - 260)
               && width is >= 28 and <= 64
               && height is >= 22 and <= 48;
    }

    private static Point SafePosition(FrameworkElement element, UIElement relativeTo)
    {
        try
        {
            return element.TranslatePoint(new Point(), relativeTo);
        }
        catch
        {
            return new Point(double.NegativeInfinity, double.NegativeInfinity);
        }
    }
}
