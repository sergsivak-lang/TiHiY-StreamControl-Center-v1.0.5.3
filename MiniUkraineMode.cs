using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Windows;

namespace TiHiY.StreamControlCenter;

internal static class MiniUkraineMode
{
    private const string UkraineTheme = "Україна";
    private static bool _themeHooked;
    private static bool _forcingTheme;

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));

        EventManager.RegisterClassHandler(
            typeof(SettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSettingsWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow main) return;
        main.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => ApplyMainWindow(main)));
    }

    private static void ApplyMainWindow(MainWindow main)
    {
        ForceUkraineTheme();
        HookThemeGuard();

        if (Equals(main.Tag, "TIHIY_MINI_UA")) return;
        main.Tag = "TIHIY_MINI_UA";
        main.Title = "TiHiY StreamControl MINI — Україна";
        main.MinWidth = 980;
        main.MinHeight = 620;
        if (main.WindowState == WindowState.Normal)
        {
            main.Width = Math.Max(1180, Math.Min(main.Width, 1460));
            main.Height = Math.Max(720, Math.Min(main.Height, 920));
        }

        if (main.FindName("DesignSurface") is not Grid originalSurface) return;
        if (main.FindName("ChatBlockPanel") is not FrameworkElement chatBlock) return;
        if (main.FindName("NotificationsBlockPanel") is not FrameworkElement notificationsBlock) return;
        if (main.FindName("DonationsBlockPanel") is not FrameworkElement donationsBlock) return;
        if (VisualTreeHelper.GetParent(originalSurface) is not Panel hostParent) return;

        Detach(chatBlock);
        Detach(notificationsBlock);
        Detach(donationsBlock);
        originalSurface.Visibility = Visibility.Collapsed;

        var root = new Grid
        {
            Margin = new Thickness(9),
            Background = FindBrush(main, "WindowGradient")
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Panel.SetZIndex(root, 10000);
        hostParent.Children.Add(root);

        var header = BuildHeader(main);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.9, GridUnitType.Star), MinWidth = 560 });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 330 });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        PrepareBlock(chatBlock);
        PrepareBlock(notificationsBlock);
        PrepareBlock(donationsBlock);

        Grid.SetColumn(chatBlock, 0);
        chatBlock.Margin = new Thickness(0, 0, 6, 0);
        content.Children.Add(chatBlock);

        var right = new Grid { Margin = new Thickness(6, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.9, GridUnitType.Star), MinHeight = 230 });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.1, GridUnitType.Star), MinHeight = 275 });
        Grid.SetColumn(right, 1);
        content.Children.Add(right);

        notificationsBlock.Margin = new Thickness(0, 0, 0, 6);
        Grid.SetRow(notificationsBlock, 0);
        right.Children.Add(notificationsBlock);

        donationsBlock.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(donationsBlock, 1);
        right.Children.Add(donationsBlock);

        FixBottomActions(chatBlock);
        FixBottomActions(notificationsBlock);
        FixBottomActions(donationsBlock);

        main.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                PrepareBlock(chatBlock);
                PrepareBlock(notificationsBlock);
                PrepareBlock(donationsBlock);
                FixBottomActions(chatBlock);
                FixBottomActions(notificationsBlock);
                FixBottomActions(donationsBlock);
            }));
    }

    private static Border BuildHeader(MainWindow main)
    {
        var border = new Border
        {
            Background = FindBrush(main, "PanelGradient"),
            BorderBrush = FindBrush(main, "Amber"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 7, 8, 7)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        border.Child = grid;

        var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock
        {
            Text = "TiHiY  STREAMCONTROL MINI",
            FontSize = 20,
            FontWeight = FontWeights.Black,
            Foreground = FindBrush(main, "Amber"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        brand.Children.Add(new TextBlock
        {
            Text = "МУЛЬТИЧАТ  •  ДОНАТИ  •  СПОВІЩЕННЯ  •  УСІ ВІДЖЕТИ",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush(main, "Muted"),
            Margin = new Thickness(1, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        grid.Children.Add(brand);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        actions.Children.Add(ActionButton(main, "КАНАЛИ", 92, () => App.Services.Windows.Show(() => new ChannelConnectionsWindow(), main)));
        actions.Children.Add(ActionButton(main, "ДОНАТИ", 92, () => App.Services.Windows.Show(() => new DonatelloWindow(), main)));
        actions.Children.Add(ActionButton(main, "СПОВІЩЕННЯ", 122, () => App.Services.Windows.Show(() => new StreamNotificationsWindow(), main)));
        actions.Children.Add(ActionButton(main, "⚙", 42, () => App.Services.Windows.Show(() => new SettingsWindow(), main), "Налаштування"));
        actions.Children.Add(ActionButton(main, "▶", 42, () => App.Services.Windows.Show(() => new OverlaySettingsWindow(), main), "Віджети / Browser Source"));

        // Єдиний комплект керування вікном. Інших комплектів MINI не створює.
        actions.Children.Add(WindowButton(main, "—", () => main.WindowState = WindowState.Minimized));
        actions.Children.Add(WindowButton(main, "□", () => main.WindowState = main.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        actions.Children.Add(WindowButton(main, "✕", main.Close));

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
            if (e.ClickCount == 2)
            {
                main.WindowState = main.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            try { main.DragMove(); } catch { }
        };

        return border;
    }

    private static Button ActionButton(MainWindow main, string text, double width, Action action, string? tooltip = null)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            MinWidth = width,
            Height = 36,
            MinHeight = 36,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = text.Length <= 2 ? 16 : 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush(main, "Text"),
            ToolTip = tooltip
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button WindowButton(MainWindow main, string text, Action action)
    {
        var button = ActionButton(main, text, 38, action);
        button.Padding = new Thickness(0);
        button.FontSize = 15;
        return button;
    }

    private static void PrepareBlock(FrameworkElement block)
    {
        block.Visibility = Visibility.Visible;
        block.Width = double.NaN;
        block.Height = double.NaN;
        block.HorizontalAlignment = HorizontalAlignment.Stretch;
        block.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private static void FixBottomActions(FrameworkElement block)
    {
        foreach (var button in FindDescendants<Button>(block))
        {
            var label = GetButtonText(button);
            if (label.Contains("ВІДКРИТИ ДОНАТИ", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("ВІДКРИТИ ПОВНИЙ ЖУРНАЛ", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("TWITCH", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("ОБИДВА", StringComparison.OrdinalIgnoreCase))
            {
                button.MinHeight = 34;
                if (double.IsNaN(button.Height) || button.Height < 34) button.Height = 34;
                button.Margin = new Thickness(Math.Max(3, button.Margin.Left), 3, Math.Max(3, button.Margin.Right), 5);

                if (button.Parent is Grid grid)
                {
                    var row = Grid.GetRow(button);
                    if (row >= 0 && row < grid.RowDefinitions.Count)
                    {
                        var current = grid.RowDefinitions[row].Height;
                        if (current.IsAbsolute && current.Value < 46)
                            grid.RowDefinitions[row].Height = new GridLength(46);
                    }
                }
            }
        }
    }

    private static string GetButtonText(Button button)
    {
        if (button.Content is string s) return s;
        if (button.Content is DependencyObject root)
            return string.Join(" ", FindDescendants<TextBlock>(root).Select(x => x.Text));
        return string.Empty;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
        }
    }

    private static Brush FindBrush(FrameworkElement owner, string key) =>
        owner.TryFindResource(key) as Brush ?? Brushes.Transparent;

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void HookThemeGuard()
    {
        if (_themeHooked) return;
        _themeHooked = true;
        App.Services.Theme.ThemeChanged += (_, _) =>
        {
            if (_forcingTheme) return;
            if (!string.Equals(App.Services.Theme.CurrentTheme, UkraineTheme, StringComparison.OrdinalIgnoreCase))
                ForceUkraineTheme();
        };
    }

    private static void ForceUkraineTheme()
    {
        if (_forcingTheme) return;
        _forcingTheme = true;
        try
        {
            if (!string.Equals(App.Services.Theme.CurrentTheme, UkraineTheme, StringComparison.OrdinalIgnoreCase))
                App.Services.Theme.Apply(UkraineTheme, save: true);
            else
                App.Services.Theme.Apply(UkraineTheme, save: false);
        }
        finally
        {
            _forcingTheme = false;
        }
    }

    private static void OnSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsWindow settings) return;
        settings.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            ForceUkraineTheme();
            LockThemeSelector(settings, "ThemeList");
            LockThemeSelector(settings, "ThemeCombo");
        }));
    }

    private static void LockThemeSelector(FrameworkElement settings, string name)
    {
        if (settings.FindName(name) is Control control)
        {
            control.IsEnabled = false;
            control.ToolTip = "TiHiY StreamControl MINI використовує одну тему — Україна.";
        }
    }
}
