using System.Runtime.CompilerServices;
using System.Windows;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

/// <summary>
/// Mirrors paid subscription/member events into the MINI multichat.
/// The external id is normalized so YouTube's following raw chat item is naturally
/// de-duplicated by ChatService. Donatello is mirrored here only when its legacy
/// "show in chat" switch is off, avoiding a double entry.
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
        var isDonatello = source.Contains("DONATELLO", StringComparison.OrdinalIgnoreCase);

        // AppServices already mirrors Donatello into chat when this option is enabled.
        if (isDonatello && App.Services.Settings.Value.DonatelloShowInChat) return;

        var platform = source.Contains("YOUTUBE", StringComparison.OrdinalIgnoreCase)
            ? "YOUTUBE"
            : source.Contains("TWITCH", StringComparison.OrdinalIgnoreCase)
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
            ExternalId = NormalizeChatId(donation, platform),
            Time = donation.Time
        });
    }

    private static string NormalizeChatId(DonationEvent donation, string platform)
    {
        var id = donation.ExternalId?.Trim() ?? string.Empty;
        if (platform == "YOUTUBE" && id.StartsWith("youtube:", StringComparison.OrdinalIgnoreCase))
            return id["youtube:".Length..];

        if (platform == "DONATELLO")
            return "money:" + donation.StableId;

        return "subscription:" + donation.StableId;
    }
}
