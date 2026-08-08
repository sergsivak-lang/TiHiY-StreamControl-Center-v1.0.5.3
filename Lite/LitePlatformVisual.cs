namespace TiHiY.StreamControlCenter;

internal static class LitePlatformVisual
{
    public static FrameworkElement BuildIcon(string? platform, double size = 18)
    {
        var p = (platform ?? string.Empty).Trim().ToUpperInvariant();
        var asset = p.Contains("TWITCH") ? "/Assets/Platforms/twitch.png"
            : p.Contains("YOUTUBE") ? "/Assets/Platforms/youtube.png"
            : p.Contains("DISCORD") ? "/Assets/Platforms/discord.png"
            : p.Contains("DONATELLO") ? "/Assets/Platforms/donatello.png"
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(asset))
        {
            try
            {
                var image = new Image
                {
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                    ToolTip = FriendlyName(p)
                };
                image.Source = new BitmapImage(new Uri(asset, UriKind.Relative));
                return image;
            }
            catch { }
        }

        var text = p.Contains("STREAMLABS") ? "S"
            : p.Contains("AIMP") ? "♪"
            : p.Contains("BOTH") || p.Contains("TWITCH + YOUTUBE") ? "◉"
            : "•";
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(p.Contains("STREAMLABS") ? Color.FromRgb(49, 194, 152) : Color.FromRgb(29, 88, 116)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = Math.Max(9, size * .66),
                FontWeight = FontWeights.Black,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = FriendlyName(p)
        };
    }

    public static StackPanel BuildTargetIcons(string? target, double size = 18)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var t = (target ?? string.Empty).Trim();
        if (t.Contains("Twitch", StringComparison.OrdinalIgnoreCase))
            panel.Children.Add(BuildIcon("TWITCH", size));
        if (t.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
        {
            if (panel.Children.Count > 0) panel.Children.Add(new Border { Width = 4 });
            panel.Children.Add(BuildIcon("YOUTUBE", size));
        }
        if (panel.Children.Count == 0) panel.Children.Add(BuildIcon(target, size));
        panel.ToolTip = string.IsNullOrWhiteSpace(t) ? "Канал" : t;
        return panel;
    }

    private static string FriendlyName(string p) => p.Contains("TWITCH") ? "Twitch"
        : p.Contains("YOUTUBE") ? "YouTube"
        : p.Contains("DISCORD") ? "Discord"
        : p.Contains("DONATELLO") ? "Donatello"
        : p.Contains("STREAMLABS") ? "Streamlabs"
        : p.Contains("AIMP") ? "AIMP"
        : p;
}