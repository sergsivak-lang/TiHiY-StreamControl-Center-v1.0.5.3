$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$path, [string]$old, [string]$new, [string]$label) {
    $text = Get-Content $path -Raw
    if (-not $text.Contains($old)) { throw "Не знайдено блок: $label у $path" }
    $text = $text.Replace($old, $new)
    Set-Content $path $text -Encoding utf8
}

# 1) MainWindow: use the existing RichChatTextBlock in the main chat.
$xamlPath = 'MainWindow.xaml'
$xaml = Get-Content $xamlPath -Raw
if (-not $xaml.Contains('xmlns:controls="clr-namespace:TiHiY.StreamControlCenter.Controls"')) {
    $xaml = $xaml.Replace(
        'xmlns:models="clr-namespace:TiHiY.StreamControlCenter.Models"',
        'xmlns:models="clr-namespace:TiHiY.StreamControlCenter.Models"' + "`r`n" + '        xmlns:controls="clr-namespace:TiHiY.StreamControlCenter.Controls"')
}
$oldChat = '<TextBlock Grid.Column="3" Text="{Binding Text}" TextWrapping="Wrap" Foreground="#DCE9F3"/>'
$newChat = '<controls:RichChatTextBlock Grid.Column="3" Message="{Binding}" IncludeUser="False" MessageBrush="#DCE9F3" HighlightBrush="#FFD329" EmoteSize="24"/>'
if ($xaml.Contains($oldChat)) { $xaml = $xaml.Replace($oldChat, $newChat) }
elseif (-not $xaml.Contains('<controls:RichChatTextBlock Grid.Column="3"')) { throw 'Не знайдено шаблон тексту головного чату.' }
Set-Content $xamlPath $xaml -Encoding utf8

# 2) Twitch IRC: parse the emotes tag into ChatMessage.Emotes.
$twitchPath = 'Services/TwitchService.cs'
$twitch = Get-Content $twitchPath -Raw
$oldReturn = '        return new ChatMessage { Platform = "TWITCH", User = user, Text = text, Role = role, ExternalId = tags.TryGetValue("id", out var id) ? id : string.Empty, AuthorId = tags.TryGetValue("user-id", out var userId) ? userId : string.Empty, Time = DateTime.Now };'
$newReturn = @'
        return new ChatMessage
        {
            Platform = "TWITCH",
            User = user,
            Text = text,
            Role = role,
            ExternalId = tags.TryGetValue("id", out var id) ? id : string.Empty,
            AuthorId = tags.TryGetValue("user-id", out var userId) ? userId : string.Empty,
            Time = DateTime.Now,
            Emotes = ParseTwitchEmotes(tags, text)
        };
'@
if ($twitch.Contains($oldReturn)) { $twitch = $twitch.Replace($oldReturn, $newReturn.TrimEnd()) }
elseif (-not $twitch.Contains('Emotes = ParseTwitchEmotes(tags, text)')) { throw 'Не знайдено створення Twitch ChatMessage.' }

$anchor = '    private static DonationEvent? ParseDonation(string line)'
$helper = @'
    private static List<ChatEmote> ParseTwitchEmotes(IReadOnlyDictionary<string, string> tags, string text)
    {
        var result = new List<ChatEmote>();
        if (!tags.TryGetValue("emotes", out var raw) || string.IsNullOrWhiteSpace(raw)) return result;

        foreach (var group in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = group.IndexOf(':');
            if (separator <= 0 || separator >= group.Length - 1) continue;
            var emoteId = group[..separator];
            foreach (var range in group[(separator + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var dash = range.IndexOf('-');
                if (dash <= 0 || dash >= range.Length - 1) continue;
                if (!int.TryParse(range[..dash], out var start) || !int.TryParse(range[(dash + 1)..], out var end)) continue;
                if (start < 0 || end < start || end >= text.Length) continue;

                result.Add(new ChatEmote
                {
                    Platform = "TWITCH",
                    Id = emoteId,
                    Name = text.Substring(start, end - start + 1),
                    Start = start,
                    End = end,
                    ImageUrl = $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emoteId)}/default/dark/2.0"
                });
            }
        }

        return result.OrderBy(x => x.Start).ThenByDescending(x => x.Length).ToList();
    }

'@
if (-not $twitch.Contains('private static List<ChatEmote> ParseTwitchEmotes')) {
    if (-not $twitch.Contains($anchor)) { throw 'Не знайдено точку вставки ParseTwitchEmotes.' }
    $twitch = $twitch.Replace($anchor, $helper + $anchor)
}
Set-Content $twitchPath $twitch -Encoding utf8

# 3) STALKER center: replace the live Ukraine content instead of hiding it by opacity.
$runtimePath = 'Services/StalkerApprovedTextureRuntime.cs'
$runtime = Get-Content $runtimePath -Raw
$fieldAnchor = '        private readonly Dictionary<Border, MetricState> _metricStates = new();'
if (-not $runtime.Contains('_centerOriginalContent')) {
    $runtime = $runtime.Replace($fieldAnchor, $fieldAnchor + "`r`n" + '        private readonly Dictionary<ContentControl, object?> _centerOriginalContent = new();')
}

$methodStart = $runtime.IndexOf('        private void ApplyExactCenterPanel(bool stalker)')
$methodEnd = $runtime.IndexOf('        private void ApplyAidaMetricLayout(bool stalker)', $methodStart)
if ($methodStart -lt 0 -or $methodEnd -lt 0) { throw 'Не знайдено ApplyExactCenterPanel.' }
$newMethod = @'
        private void ApplyExactCenterPanel(bool stalker)
        {
            var footer = FindNamed<Grid>("FooterBlocksGrid");
            var center = footer?.Children.OfType<ContentControl>().FirstOrDefault(x => Grid.GetColumn(x) == 2);
            if (center is null) return;

            SaveContent(center);
            if (!_centerOriginalContent.ContainsKey(center))
                _centerOriginalContent[center] = center.Content;

            if (stalker)
            {
                center.Background = StalkerApprovedAssets.NewStretchBrush("center-zone-panel-exact.png", 1.0);
                center.BorderBrush = Brushes.Transparent;
                center.BorderThickness = new Thickness(0);
                center.Padding = new Thickness(0);

                if (center.Content is not Image image || image.Tag as string != "StalkerCenterArtwork")
                {
                    image = StalkerApprovedAssets.NewImage("center-zone-banner.png", Stretch.UniformToFill);
                    image.Tag = "StalkerCenterArtwork";
                    image.HorizontalAlignment = HorizontalAlignment.Stretch;
                    image.VerticalAlignment = VerticalAlignment.Stretch;
                    image.Margin = new Thickness(6);
                    center.Content = image;
                }
            }
            else
            {
                if (_centerOriginalContent.TryGetValue(center, out var original))
                    center.Content = original;
                RestoreContent(center);
            }
        }

'@
$runtime = $runtime[..$methodStart] + $newMethod + $runtime[$methodEnd..]
Set-Content $runtimePath $runtime -Encoding utf8

# 4) Architecture notes for future themes.
@'
# Theme Engine v1

## Mandatory rules for every theme
1. A theme may only change controls while it is active.
2. Theme changes are event-driven: window Loaded, ThemeChanged, and creation of a new themed window. No polling timers.
3. Every replaced Content value must be stored and restored.
4. DynamicResource-based values must be restored with ClearValue where appropriate; permanent XAML content must be restored from an explicit snapshot.
5. Platform functionality such as Twitch emotes belongs to the shared chat layer, not to one theme.
6. Each theme owns its own textures, typography, button templates, and center module.
7. Release validation must test Theme A -> Theme B -> Theme A and window maximize/restore.

## STALKER implementation notes
- Exact assets live under Assets/Themes/StalkerApproved.
- Center content is replaced while STALKER is active and restored when leaving it.
- Main chat uses RichChatTextBlock; TwitchService parses IRC emotes tags.
- No visual polling timers are allowed in STALKER runtimes.
'@ | Set-Content 'THEME-ENGINE-V1-UA.md' -Encoding utf8

Write-Host 'Theme Engine v1 patch applied.' -ForegroundColor Green
