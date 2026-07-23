using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TiHiY.StreamControlCenter.Controls;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Upgrades the legacy MainWindow chat row at runtime without changing the
/// approved panel XAML: plain message TextBlocks become RichChatTextBlock and a
/// dedicated author-avatar column is inserted between platform and user name.
/// </summary>
internal static class RichMainChatBootstrap
{
    private static readonly DependencyProperty AvatarColumnAppliedProperty = DependencyProperty.RegisterAttached(
        "AvatarColumnApplied",
        typeof(bool),
        typeof(RichMainChatBootstrap),
        new PropertyMetadata(false));

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnTextBlockLoaded));
    }

    private static void OnTextBlockLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is RichChatTextBlock || sender is not TextBlock textBlock)
            return;

        if (textBlock.DataContext is not ChatMessage)
            return;

        var binding = BindingOperations.GetBinding(textBlock, TextBlock.TextProperty);
        var path = binding?.Path?.Path;
        if (path is not nameof(ChatMessage.Text) and not nameof(ChatMessage.DisplayText))
            return;

        if (VisualTreeHelper.GetParent(textBlock) is not Grid rowGrid)
            return;

        var originalIndex = rowGrid.Children.IndexOf(textBlock);
        if (originalIndex < 0)
            return;

        var rich = new RichChatTextBlock
        {
            IncludeUser = false,
            EmoteSize = Math.Max(20, textBlock.FontSize + 7),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = textBlock.FontFamily,
            FontSize = textBlock.FontSize,
            FontStyle = textBlock.FontStyle,
            FontWeight = textBlock.FontWeight,
            Margin = textBlock.Margin,
            Padding = textBlock.Padding,
            HorizontalAlignment = textBlock.HorizontalAlignment,
            VerticalAlignment = textBlock.VerticalAlignment,
            MinWidth = textBlock.MinWidth,
            MaxWidth = textBlock.MaxWidth,
            MinHeight = textBlock.MinHeight,
            MaxHeight = textBlock.MaxHeight,
            ToolTip = textBlock.ToolTip
        };

        BindingOperations.SetBinding(rich, RichChatTextBlock.MessageProperty, new Binding());
        BindingOperations.SetBinding(rich, RichChatTextBlock.MessageBrushProperty, new Binding(nameof(ChatMessage.Foreground))
        {
            Mode = BindingMode.OneWay
        });

        Grid.SetRow(rich, Grid.GetRow(textBlock));
        Grid.SetRowSpan(rich, Grid.GetRowSpan(textBlock));
        Grid.SetColumn(rich, Grid.GetColumn(textBlock));
        Grid.SetColumnSpan(rich, Grid.GetColumnSpan(textBlock));

        rowGrid.Children.RemoveAt(originalIndex);
        rowGrid.Children.Insert(originalIndex, rich);
        EnsureAvatarColumn(rowGrid);
    }

    private static void EnsureAvatarColumn(Grid rowGrid)
    {
        if ((bool)rowGrid.GetValue(AvatarColumnAppliedProperty))
            return;

        // The stock multichat row is: time | platform | user | message.
        // Restrict the upgrade to that exact shape so unrelated ChatMessage views
        // are not rearranged.
        if (rowGrid.ColumnDefinitions.Count != 4)
            return;

        var hasPlatformCell = rowGrid.Children.Cast<UIElement>().Any(child => Grid.GetColumn(child) == 1);
        var hasUserCell = rowGrid.Children.OfType<TextBlock>().Any(child =>
            child is not RichChatTextBlock &&
            Grid.GetColumn(child) == 2 &&
            BindingOperations.GetBinding(child, TextBlock.TextProperty)?.Path?.Path == nameof(ChatMessage.User));
        var hasMessageCell = rowGrid.Children.OfType<RichChatTextBlock>().Any(child => Grid.GetColumn(child) == 3);
        if (!hasPlatformCell || !hasUserCell || !hasMessageCell)
            return;

        rowGrid.SetValue(AvatarColumnAppliedProperty, true);

        foreach (UIElement child in rowGrid.Children)
        {
            var column = Grid.GetColumn(child);
            if (column >= 2)
                Grid.SetColumn(child, column + 1);
        }

        rowGrid.ColumnDefinitions.Insert(2, new ColumnDefinition { Width = new GridLength(36) });

        var avatar = new ChatAuthorAvatar
        {
            Margin = new Thickness(2, 0, 6, 0)
        };
        BindingOperations.SetBinding(avatar, ChatAuthorAvatar.MessageProperty, new Binding());
        Grid.SetColumn(avatar, 2);
        rowGrid.Children.Add(avatar);
    }
}
