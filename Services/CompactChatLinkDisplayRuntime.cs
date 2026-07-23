using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Keeps the original chat message URL in ChatMessage.Text while presenting
/// only a clean host name in live chat rows (for example example.com).
/// </summary>
internal static class CompactChatLinkDisplayBootstrap
{
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
        if (sender is not TextBlock textBlock || textBlock.DataContext is not ChatMessage)
            return;

        var binding = BindingOperations.GetBinding(textBlock, TextBlock.TextProperty);
        if (binding?.Path?.Path is not "Text")
            return;

        BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, new Binding(nameof(ChatMessage.DisplayText))
        {
            Mode = BindingMode.OneWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
    }
}
