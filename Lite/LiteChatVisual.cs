using System.Windows.Documents;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter;

internal static class LiteChatVisual
{
    public static FrameworkElement BuildHudRow(ChatMessage m, double fontSize, string textColor, string userFallbackColor)
    {
        var root = new Border
        {
            Padding = new Thickness(5, 4, 5, 4),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(60, 3, 16, 28))
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Child = grid;

        var icon = LitePlatformVisual.BuildIcon(m.Platform, Math.Max(15, fontSize - 2));
        if (icon is FrameworkElement iconElement)
        {
            iconElement.Margin = new Thickness(0, 2, 6, 0);
            iconElement.VerticalAlignment = VerticalAlignment.Top;
        }
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var line = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            Foreground = Parse(textColor, Brushes.White),
            LineHeight = fontSize * 1.35,
            VerticalAlignment = VerticalAlignment.Top
        };
        line.Inlines.Add(new Run((m.User ?? string.Empty) + ": ")
        {
            FontSize = Math.Max(11, fontSize - 1),
            Foreground = Parse(m.Foreground, Parse(userFallbackColor, Brushes.DeepSkyBlue)),
            FontWeight = FontWeights.Bold
        });
        AppendMessageInlines(line, m, fontSize);
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);

        return root;
    }

    public static TextBlock BuildText(ChatMessage m, double fontSize, string textColor)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            Foreground = Parse(textColor, Brushes.White),
            LineHeight = fontSize * 1.35
        };
        AppendMessageInlines(tb, m, fontSize);
        return tb;
    }

    private static void AppendMessageInlines(TextBlock tb, ChatMessage m, double fontSize)
    {
        var text = m.Text ?? string.Empty;
        var emotes = (m.Emotes ?? new List<ChatEmote>())
            .Where(x => x.Start >= 0 && x.End >= x.Start && x.End < text.Length)
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.Length)
            .ToList();
        var pos = 0;
        foreach (var e in emotes)
        {
            if (e.Start < pos) continue;
            if (e.Start > pos) tb.Inlines.Add(new Run(text[pos..e.Start]));
            try
            {
                var img = new Image
                {
                    Width = fontSize * 1.35,
                    Height = fontSize * 1.35,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(2, 0, 2, -4),
                    ToolTip = e.Name
                };
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(e.ImageUrl, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                img.Source = bmp;
                tb.Inlines.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center });
            }
            catch { tb.Inlines.Add(new Run(e.Name)); }
            pos = e.End + 1;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
    }

    private static Brush Parse(string value, Brush fallback)
    {
        try { return new BrushConverter().ConvertFromString(value) as Brush ?? fallback; }
        catch { return fallback; }
    }
}
