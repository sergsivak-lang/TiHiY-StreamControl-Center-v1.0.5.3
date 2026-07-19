$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$path, [string]$old, [string]$new, [string]$label) {
    $text = Get-Content $path -Raw -Encoding UTF8
    if (-not $text.Contains($old)) { throw "Не знайдено блок: $label у $path" }
    $text = $text.Replace($old, $new)
    Set-Content $path $text -Encoding UTF8
}

# 1) Twitch IRC: preserve native emote metadata.
$twitchPath = 'Services/TwitchService.cs'
$twitch = Get-Content $twitchPath -Raw -Encoding UTF8
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
if ($twitch.Contains($oldReturn)) {
    $twitch = $twitch.Replace($oldReturn, $newReturn.TrimEnd())
}
elseif (-not $twitch.Contains('Emotes = ParseTwitchEmotes(tags, text)')) {
    throw 'Не знайдено створення Twitch ChatMessage.'
}

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
Set-Content $twitchPath $twitch -Encoding UTF8

# 2) Browser/OBS overlay: send emotes through JSON.
$overlayPath = 'Services/OverlayServer.cs'
$overlay = Get-Content $overlayPath -Raw -Encoding UTF8
$oldApi = @'
                    foreground = m.Foreground,
                    highlighted = m.IsHighlighted
'@
$newApi = @'
                    foreground = m.Foreground,
                    highlighted = m.IsHighlighted,
                    emotes = (m.Emotes ?? new List<ChatEmote>())
                        .Where(e => e.Start >= 0 && e.End >= e.Start && e.End < (m.Text ?? string.Empty).Length && !string.IsNullOrWhiteSpace(e.ImageUrl))
                        .OrderBy(e => e.Start)
                        .Select(e => new { start = e.Start, end = e.End, name = e.Name, url = e.ImageUrl })
'@
if ($overlay.Contains($oldApi)) {
    $overlay = $overlay.Replace($oldApi, $newApi)
}
elseif (-not $overlay.Contains('emotes = (m.Emotes ?? new List<ChatEmote>())')) {
    throw 'Не знайдено JSON-модель /api/chat.'
}

# Add CSS for inline emotes and compact links.
$oldCss = '.text{display:block;margin-top:3px;word-break:break-word;white-space:pre-wrap}.highlight .text,.highlight .user{color:#FFD329!important;font-weight:900}'
$newCss = '.text{display:block;margin-top:3px;word-break:break-word;white-space:pre-wrap}.emote{display:inline-block;width:1.55em;height:1.55em;object-fit:contain;vertical-align:-.38em;margin:0 .08em}.chatLink{color:inherit;text-decoration:underline;text-decoration-thickness:1px;text-underline-offset:2px}.highlight .text,.highlight .user{color:#FFD329!important;font-weight:900}'
if ($overlay.Contains($oldCss)) {
    $overlay = $overlay.Replace($oldCss, $newCss)
}
elseif (-not $overlay.Contains('.emote{display:inline-block')) {
    throw 'Не знайдено CSS чату.'
}

$oldMake = "function makeMessage(m){const box=document.createElement('div');box.className='msg'+(m.highlighted?' highlight':'');const line=document.createElement('div');line.className='line';line.append(icon(m.platform));const content=document.createElement('div');content.className='content';const user=document.createElement('span');user.className='user';user.style.color=m.foreground||'__USER__';user.textContent=(m.user||'Глядач')+':';content.append(user);const text=document.createElement('span');text.className='text';text.textContent=m.text||'';content.append(text);line.append(content);box.append(line);return box}"
$newMake = @'
function appendTextWithLinks(host,value){const re=/https?:\/\/[^\s]+/gi;let cursor=0;for(const match of value.matchAll(re)){if(match.index>cursor)host.append(document.createTextNode(value.slice(cursor,match.index)));const raw=match[0];const clean=raw.replace(/[.,;:!?)\]}]+$/,'');const suffix=raw.slice(clean.length);try{const uri=new URL(clean);const a=document.createElement('a');a.className='chatLink';a.href=uri.href;a.target='_blank';a.rel='noopener noreferrer';a.textContent=uri.hostname.replace(/^www\./i,'');a.title=clean;host.append(a);if(suffix)host.append(document.createTextNode(suffix))}catch{host.append(document.createTextNode(raw))}cursor=match.index+raw.length}if(cursor<value.length)host.append(document.createTextNode(value.slice(cursor)))}
function renderMessageText(host,m){const value=m.text||'';const emotes=Array.isArray(m.emotes)?m.emotes.filter(e=>Number.isInteger(e.start)&&Number.isInteger(e.end)&&e.start>=0&&e.end>=e.start&&e.end<value.length&&e.url).sort((a,b)=>a.start-b.start||b.end-a.end):[];let cursor=0;for(const e of emotes){if(e.start<cursor)continue;appendTextWithLinks(host,value.slice(cursor,e.start));const img=document.createElement('img');img.className='emote';img.src=e.url;img.alt=e.name||value.slice(e.start,e.end+1);img.title=img.alt;img.referrerPolicy='no-referrer';img.addEventListener('error',()=>img.replaceWith(document.createTextNode(img.alt)));host.append(img);cursor=e.end+1}appendTextWithLinks(host,value.slice(cursor))}
function makeMessage(m){const box=document.createElement('div');box.className='msg'+(m.highlighted?' highlight':'');const line=document.createElement('div');line.className='line';line.append(icon(m.platform));const content=document.createElement('div');content.className='content';const user=document.createElement('span');user.className='user';user.style.color=m.foreground||'__USER__';user.textContent=(m.user||'Глядач')+':';content.append(user);const text=document.createElement('span');text.className='text';renderMessageText(text,m);content.append(text);line.append(content);box.append(line);return box}
'@
if ($overlay.Contains($oldMake)) {
    $overlay = $overlay.Replace($oldMake, $newMake.TrimEnd())
}
elseif (-not $overlay.Contains('function renderMessageText(host,m)')) {
    throw 'Не знайдено makeMessage у Browser Overlay.'
}

Set-Content $overlayPath $overlay -Encoding UTF8
Write-Host 'Overlay chat fix v1 applied.' -ForegroundColor Green
