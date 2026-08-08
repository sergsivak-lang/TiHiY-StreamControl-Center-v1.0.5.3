using System.Windows.Documents;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

internal static class LiteChatVisual
{
    public static FrameworkElement BuildHudRow(ChatMessage m, double fontSize)
    {
        var root = new Border { Padding = new Thickness(5,4,5,4), Margin = new Thickness(0,1,0,1), CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromArgb(60,3,16,28)) };
        var stack = new StackPanel(); root.Child = stack;
        var header = new TextBlock { FontSize = Math.Max(11,fontSize-3) };
        var platform = m.Platform.StartsWith("TW",StringComparison.OrdinalIgnoreCase) ? "TW" : m.Platform.StartsWith("YOU",StringComparison.OrdinalIgnoreCase) ? "YT" : "♥";
        header.Inlines.Add(new Run(platform + "  ") { Foreground = m.Platform.StartsWith("TW",StringComparison.OrdinalIgnoreCase) ? Brushes.MediumPurple : m.Platform.StartsWith("YOU",StringComparison.OrdinalIgnoreCase) ? Brushes.OrangeRed : Brushes.Gold, FontWeight = FontWeights.Bold });
        header.Inlines.Add(new Run(m.User) { Foreground = Parse(m.Foreground, Brushes.DeepSkyBlue), FontWeight = FontWeights.Bold });
        stack.Children.Add(header);
        stack.Children.Add(BuildText(m, fontSize));
        return root;
    }

    public static TextBlock BuildText(ChatMessage m, double fontSize)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = fontSize, Foreground = Brushes.White, LineHeight = fontSize * 1.35 };
        var text = m.Text ?? string.Empty;
        var emotes = (m.Emotes ?? new List<ChatEmote>()).Where(x => x.Start >= 0 && x.End >= x.Start && x.End < text.Length).OrderBy(x => x.Start).ThenByDescending(x=>x.Length).ToList();
        var pos = 0;
        foreach (var e in emotes)
        {
            if (e.Start < pos) continue;
            if (e.Start > pos) tb.Inlines.Add(new Run(text[pos..e.Start]));
            try
            {
                var img = new Image { Width = fontSize * 1.35, Height = fontSize * 1.35, Stretch = Stretch.Uniform, Margin = new Thickness(2,0,2,-4), ToolTip = e.Name };
                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.UriSource = new Uri(e.ImageUrl, UriKind.Absolute); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); img.Source = bmp;
                tb.Inlines.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center });
            }
            catch { tb.Inlines.Add(new Run(e.Name)); }
            pos = e.End + 1;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
        return tb;
    }

    private static Brush Parse(string value, Brush fallback)
    {
        try { return new BrushConverter().ConvertFromString(value) as Brush ?? fallback; } catch { return fallback; }
    }
}
