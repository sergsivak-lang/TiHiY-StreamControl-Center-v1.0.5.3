using System.Globalization;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

public partial class GameOverlaySettingsWindow : Window
{
    private readonly LiteCoreService _core;

    public GameOverlaySettingsWindow()
    {
        InitializeComponent();
        _core = LiteApp.Core;
        LoadValues();
    }

    private void LoadValues()
    {
        var s = _core.Settings.Value;
        AutoStartCheck.IsChecked = s.LocalChatOverlayAutoStart;
        ClickThroughCheck.IsChecked = s.LocalChatOverlayClickThrough;
        ExcludeCaptureCheck.IsChecked = _core.Preferences.Value.HudExcludeFromCapture;
        OpacitySlider.Value = Math.Clamp(s.LocalChatOverlayBackgroundOpacity, 0, .85);
        FontSizeBox.Text = s.LocalChatOverlayFontSize.ToString("0.#", CultureInfo.InvariantCulture);
        MaxMessagesBox.Text = s.LocalChatOverlayMaxMessages.ToString(CultureInfo.InvariantCulture);
        TextColorBox.Text = s.LocalChatOverlayTextColor;
        UserColorBox.Text = s.LocalChatOverlayUserColor;
        UpdateOpacityText();
    }

    private void SaveValues()
    {
        var s = _core.Settings.Value;
        s.LocalChatOverlayAutoStart = AutoStartCheck.IsChecked == true;
        s.LocalChatOverlayClickThrough = ClickThroughCheck.IsChecked == true;
        s.LocalChatOverlayBackgroundOpacity = Math.Clamp(OpacitySlider.Value, 0, .85);
        s.LocalChatOverlayFontSize = ParseDouble(FontSizeBox.Text, s.LocalChatOverlayFontSize, 11, 42);
        s.LocalChatOverlayMaxMessages = ParseInt(MaxMessagesBox.Text, s.LocalChatOverlayMaxMessages, 3, 30);
        s.LocalChatOverlayTextColor = NormalizeColor(TextColorBox.Text, "#F2FAFF");
        s.LocalChatOverlayUserColor = NormalizeColor(UserColorBox.Text, "#55C8FF");
        _core.Preferences.Value.HudExcludeFromCapture = ExcludeCaptureCheck.IsChecked == true;
        _core.SettingsService.Save(s);
        _core.Preferences.Save();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SaveValues();
        if (Owner is MainWindow main) main.ApplyOverlaySettings();
        StatusText.Text = "Налаштування overlay застосовано";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        SaveValues();
        if (Owner is MainWindow main) main.ShowOverlay();
        StatusText.Text = "Overlay відкрито";
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main) main.HideOverlay();
        StatusText.Text = "Overlay приховано";
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _core.Settings.Value.WindowPlacements.Remove("LocalChatOverlay");
        var p = _core.Preferences.Value;
        p.HudLeft = 24; p.HudTop = 180; p.HudWidth = 620; p.HudHeight = 420;
        _core.SettingsService.Save(_core.Settings.Value);
        _core.Preferences.Save();
        if (Owner is MainWindow main)
        {
            main.HideOverlay();
            main.ShowOverlay();
        }
        StatusText.Text = "Позицію overlay скинуто";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateOpacityText();
    private void UpdateOpacityText() { if (OpacityText is not null) OpacityText.Text = $"{OpacitySlider.Value:P0}"; }
    private static double ParseDouble(string text, double fallback, double min, double max) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;
    private static int ParseInt(string text, int fallback, int min, int max) => int.TryParse(text, out var v) ? Math.Clamp(v, min, max) : fallback;
    private static string NormalizeColor(string text, string fallback)
    {
        var value = (text ?? string.Empty).Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        return (value.Length is 7 or 9) && value.Skip(1).All(Uri.IsHexDigit) ? value.ToUpperInvariant() : fallback;
    }
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
