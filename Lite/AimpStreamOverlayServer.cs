using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public sealed class AimpStreamOverlayServer : IAsyncDisposable
{
    private readonly AimpNowPlayingService _aimp;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;
    public string OverlayUrl => $"http://127.0.0.1:{Port}/overlay/now-playing";
    public string SettingsUrl => $"http://127.0.0.1:{Port}/settings";

    public AimpStreamOverlayServer(AimpNowPlayingService aimp) => _aimp = aimp;

    public Task StartAsync(int preferredPort)
    {
        if (_listener is not null) return Task.CompletedTask;
        var firstPort = Math.Clamp(preferredPort, 1025, 65520);
        Exception? last = null;
        for (var i = 0; i < 8; i++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, firstPort + i);
                listener.Start();
                Port = firstPort + i;
                _listener = listener;
                _cts = new CancellationTokenSource();
                _loop = AcceptLoopAsync(_cts.Token);
                return Task.CompletedTask;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("Не вдалося відкрити локальний порт AIMP Now Playing.", last);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { await Task.Delay(150, token).ConfigureAwait(false); }
                catch { break; }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
        {
            var first = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(first)) return;
            string? line;
            do { line = await reader.ReadLineAsync(token).ConfigureAwait(false); } while (!string.IsNullOrEmpty(line));

            var parts = first.Split(' ');
            var target = parts.Length > 1 ? parts[1] : "/";
            var uri = new Uri($"http://127.0.0.1:{Port}{target}");
            var path = uri.AbsolutePath.ToLowerInvariant();

            if (path is "/" or "/overlay/now-playing")
            {
                await RespondTextAsync(stream, "text/html; charset=utf-8", BuildWidgetHtml(uri), token).ConfigureAwait(false);
                return;
            }
            if (path == "/settings")
            {
                await RespondTextAsync(stream, "text/html; charset=utf-8", BuildSettingsHtml(), token).ConfigureAwait(false);
                return;
            }
            if (path == "/api/now-playing")
            {
                var t = _aimp.Read();
                var payload = new
                {
                    active = t.Active && !string.IsNullOrWhiteSpace(t.Title),
                    title = t.Title,
                    artist = t.Artist,
                    album = t.Album,
                    positionSeconds = t.PositionSeconds,
                    durationSeconds = t.DurationSeconds,
                    isPlaying = t.IsPlaying,
                    coverVersion = t.CoverVersion,
                    integrationMode = t.IntegrationMode
                };
                await RespondTextAsync(stream, "application/json; charset=utf-8", JsonSerializer.Serialize(payload), token).ConfigureAwait(false);
                return;
            }
            if (path == "/api/cover")
            {
                var cover = _aimp.ReadCover();
                if (cover.Data.Length == 0)
                {
                    await RespondBytesAsync(stream, "image/svg+xml; charset=utf-8", Encoding.UTF8.GetBytes(PlaceholderSvg), token).ConfigureAwait(false);
                    return;
                }
                await RespondBytesAsync(stream, cover.ContentType, cover.Data, token, 86400).ConfigureAwait(false);
                return;
            }
            if (path == "/health")
            {
                await RespondTextAsync(stream, "text/plain; charset=utf-8", "TiHiY AIMP overlay OK", token).ConfigureAwait(false);
                return;
            }
            await RespondTextAsync(stream, "text/plain; charset=utf-8", "Not found", token, "404 Not Found").ConfigureAwait(false);
        }
    }

    private static string BuildWidgetHtml(Uri uri)
    {
        var width = ClampInt(uri, "width", 620, 360, 1400);
        var height = ClampInt(uri, "height", 102, 76, 220);
        var cover = ClampInt(uri, "cover", 80, 50, 160);
        var bg = ClampDouble(uri, "bg", .68, 0, 1);
        var opacity = ClampDouble(uri, "opacity", 1, .15, 1);
        var html = WidgetTemplate
            .Replace("__WIDTH__", width.ToString(CultureInfo.InvariantCulture))
            .Replace("__HEIGHT__", height.ToString(CultureInfo.InvariantCulture))
            .Replace("__COVER__", cover.ToString(CultureInfo.InvariantCulture))
            .Replace("__BG__", bg.ToString("0.00", CultureInfo.InvariantCulture))
            .Replace("__OPACITY__", opacity.ToString("0.00", CultureInfo.InvariantCulture));
        return html;
    }

    private string BuildSettingsHtml() => SettingsTemplate.Replace("__PORT__", Port.ToString(CultureInfo.InvariantCulture));

    private static string? Query(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = pair.Split('=', 2);
            if (p.Length == 2 && string.Equals(p[0], name, StringComparison.OrdinalIgnoreCase)) return Uri.UnescapeDataString(p[1]);
        }
        return null;
    }

    private static int ClampInt(Uri uri, string name, int fallback, int min, int max) => int.TryParse(Query(uri, name), out var v) ? Math.Clamp(v, min, max) : fallback;
    private static double ClampDouble(Uri uri, string name, double fallback, double min, double max) => double.TryParse(Query(uri, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;

    private static async Task RespondTextAsync(NetworkStream stream, string contentType, string body, CancellationToken token, string status = "200 OK") =>
        await RespondBytesAsync(stream, contentType, Encoding.UTF8.GetBytes(body), token, 0, status).ConfigureAwait(false);

    private static async Task RespondBytesAsync(NetworkStream stream, string contentType, byte[] bytes, CancellationToken token, int cacheSeconds = 0, string status = "200 OK")
    {
        var cache = cacheSeconds > 0 ? $"public, max-age={cacheSeconds}" : "no-store";
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nCache-Control: {cache}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener is null) return;
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        _listener = null;
        if (_loop is not null) try { await _loop.ConfigureAwait(false); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    private const string WidgetTemplate = """
<!doctype html><html lang="uk"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0;background:transparent;overflow:hidden;font-family:"Segoe UI",sans-serif;color:#f4f8ff}#card{position:absolute;left:6px;bottom:6px;width:min(__WIDTH__px,calc(100vw - 12px));height:__HEIGHT__px;box-sizing:border-box;padding:8px 10px;display:grid;grid-template-columns:__COVER__px minmax(0,1fr);gap:10px;border-radius:10px;background:linear-gradient(135deg,rgba(3,18,34,__BG__),rgba(5,38,64,__BG__));border:1px solid rgba(67,205,255,.46);border-left:4px solid #FFD329;box-shadow:0 6px 18px rgba(0,0,0,.24);opacity:0;transform:translateY(6px);transition:.2s;overflow:hidden}#card.on{opacity:__OPACITY__;transform:none}.cover{width:__COVER__px;height:__COVER__px;align-self:center;border-radius:7px;object-fit:cover;background:#051221;border:1px solid rgba(255,211,41,.5)}.content{min-width:0;display:flex;flex-direction:column;justify-content:center}.label{font-size:9px;font-weight:900;letter-spacing:1.4px;color:#FFD329}.title{font-size:19px;line-height:1.12;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:1px}.artist{font-size:13px;font-weight:700;color:#bfe9ff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.timeline{display:grid;grid-template-columns:auto 1fr auto;gap:6px;align-items:center;margin-top:6px}.time{font:9px Consolas,monospace;color:#b7cfdd}.bar{height:4px;border-radius:999px;background:rgba(255,255,255,.15);overflow:hidden}.fill{height:100%;width:0;background:linear-gradient(90deg,#43CDFF,#FFD329)}
</style></head><body><div id="card"><img id="cover" class="cover"><div class="content"><div class="label">AIMP • ЗАРАЗ ГРАЄ</div><div id="title" class="title"></div><div id="artist" class="artist"></div><div class="timeline"><span id="pos" class="time">0:00</span><div class="bar"><div id="fill" class="fill"></div></div><span id="dur" class="time">0:00</span></div></div></div><script>
let m=null,stamp=0,cv=-1;const $=x=>document.getElementById(x);const fmt=v=>{v=Math.max(0,Number(v)||0);return Math.floor(v/60)+':'+String(Math.floor(v%60)).padStart(2,'0')};function frame(){if(m){let p=Number(m.positionSeconds)||0;if(m.isPlaying)p+=(performance.now()-stamp)/1000;const d=Number(m.durationSeconds)||0;if(d>0)p=Math.min(p,d);$('pos').textContent=fmt(p);$('dur').textContent=fmt(d);$('fill').style.width=(d>0?Math.min(100,p/d*100):0)+'%'}requestAnimationFrame(frame)}async function update(){try{const n=await(await fetch('/api/now-playing',{cache:'no-store'})).json();m=n;stamp=performance.now();$('card').classList.toggle('on',!!n.active);$('title').textContent=n.title||'';$('artist').textContent=n.artist||'';if(Number(n.coverVersion)!==cv){cv=Number(n.coverVersion)||0;$('cover').src='/api/cover?v='+cv}}catch(e){}}setInterval(update,1000);update();requestAnimationFrame(frame);
</script></body></html>
""";

    private const string SettingsTemplate = """
<!doctype html><html lang="uk"><head><meta charset="utf-8"><style>*{box-sizing:border-box}body{margin:0;padding:20px;background:#06111e;color:#edf8ff;font:14px "Segoe UI"}.p{max-width:920px;margin:auto;background:#0a1d2e;border:1px solid #174866;border-left:5px solid #ffd329;border-radius:10px;padding:18px}h2{margin:0 0 16px;color:#ffd329}.g{display:grid;grid-template-columns:190px 1fr 70px;gap:10px;align-items:center;margin:10px 0}.v{color:#43cdff;font-weight:800;text-align:right}.prev{height:180px;margin-top:16px;border:1px solid #174866;background:#111;overflow:hidden}.prev iframe{width:100%;height:100%;border:0}.url{width:100%;margin-top:14px;padding:10px;background:#061522;color:white;border:1px solid #174866}button{margin-top:10px;padding:9px 14px;background:#123954;color:white;border:1px solid #ffd329;border-radius:6px}</style></head><body><div class="p"><h2>AIMP NOW PLAYING</h2><div class="g"><span>Прозорість фону</span><input id="bg" type="range" min="0" max="100" value="68"><span id="bgv" class="v"></span></div><div class="g"><span>Прозорість віджета</span><input id="op" type="range" min="15" max="100" value="100"><span id="opv" class="v"></span></div><div class="g"><span>Висота</span><input id="h" type="range" min="76" max="180" value="102"><span id="hv" class="v"></span></div><div class="g"><span>Ширина</span><input id="w" type="range" min="360" max="1100" value="620"><span id="wv" class="v"></span></div><div class="g"><span>Обкладинка</span><input id="c" type="range" min="50" max="140" value="80"><span id="cv" class="v"></span></div><div class="prev"><iframe id="f"></iframe></div><input id="u" class="url" readonly><button id="copy">СКОПІЮВАТИ URL ДЛЯ OBS</button></div><script>const bg=document.getElementById('bg'),op=document.getElementById('op'),h=document.getElementById('h'),w=document.getElementById('w'),c=document.getElementById('c'),u=document.getElementById('u'),f=document.getElementById('f');function upd(){bgv.textContent=bg.value+'%';opv.textContent=op.value+'%';hv.textContent=h.value+' px';wv.textContent=w.value+' px';cv.textContent=c.value+' px';const x='http://127.0.0.1:__PORT__/overlay/now-playing?bg='+(+bg.value/100).toFixed(2)+'&opacity='+(+op.value/100).toFixed(2)+'&height='+h.value+'&width='+w.value+'&cover='+c.value;u.value=x;f.src=x+'&t='+Date.now()}[bg,op,h,w,c].forEach(x=>x.oninput=upd);copy.onclick=()=>navigator.clipboard.writeText(u.value);upd();</script></body></html>
""";

    private const string PlaceholderSvg = """
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256"><rect width="256" height="256" rx="18" fill="#06182b"/><path d="M162 48v112.5a42 42 0 1 1-18-34V75l-70 15v86.5a42 42 0 1 1-18-34V76z" fill="#FFD329" opacity=".92"/></svg>
""";
}
