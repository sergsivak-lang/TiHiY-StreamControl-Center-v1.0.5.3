using System.Runtime.CompilerServices;
using System.Windows;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

/// <summary>
/// Mirrors paid subscription events into the MINI multichat when the source does
/// not already provide a native chat item. YouTube liveChatMessages already emits
/// the original member/gifting message; keeping that original item is important
/// because the rich-content resolver can attach platform emoji and gift visuals.
/// </summary>
internal static class SubscriptionChatBridge
{
    private static bool _hooked;

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
        if (_hooked || sender is not MainWindow) return;
        _hooked = true;
        App.Services.Donations.DonationAdded += DonationAdded;
    }

    private static void DonationAdded(object? sender, DonationEvent donation)
    {
        if (donation.IsHistorical || donation.IsReplay || donation.IsTest) return;
        if (!donation.Kind.Equals("SUBSCRIPTION", StringComparison.OrdinalIgnoreCase)) return;

        var source = donation.Source ?? string.Empty;

        // YouTubeService itself emits the native live-chat item for new members,
        // milestones and membership gifting. Do not insert a generic item first,
        // otherwise ChatService de-duplication would discard the richer native one.
        if (source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase)) return;

        var isDonatello = source.Contains("DONATELLO", StringComparison.OrdinalIgnoreCase);
        if (isDonatello && App.Services.Settings.Value.DonatelloShowInChat) return;

        var platform = source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase)
            ? "TWITCH"
            : isDonatello ? "DONATELLO" : "SUBSCRIPTION";

        var text = string.IsNullOrWhiteSpace(donation.Message)
            ? "⭐ Нова платна підписка"
            : $"⭐ Нова платна підписка • {donation.Message.Trim()}";

        App.Services.Chat.AddIncoming(new ChatMessage
        {
            Platform = platform,
            User = string.IsNullOrWhiteSpace(donation.User) ? platform : donation.User,
            Text = text,
            Role = "Subscriber",
            ExternalId = platform == "DONATELLO" ? "money:" + donation.StableId : "subscription:" + donation.StableId,
            Time = donation.Time
        });
    }
}
