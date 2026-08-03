using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Tiny dedicated server for the AIMP stream widget.
/// The widget supports URL-controlled dimensions and opacity plus a built-in settings page.
/// </summary>
public sealed class AimpOverlayServer : IAsyncDisposable
{
    private readonly AimpNowPlayingService _aimp;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    public AimpOverlayServer(AimpNowPlayingService aimp) => _aimp = aimp;

    public Task StartAsync(int preferredPort)
    {
        if (_listener is not null) return Task.CompletedTask;
        var firstPort = Math.Clamp(preferredPort, 1025, 65525);
        Exception? last = null;
        for (var offset = 0; offset < 10; offset++)
        {
            var port = firstPort + offset;
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Port = port;
                _listener = listener;
                _cts = new CancellationTokenSource();
                _loop = AcceptLoopAsync(_cts.Token);
                return Task.CompletedTask;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("Не вдалося відкрити порт для AIMP Now Playing overlay.", last);
    }

    public async Task RestartAsync(int preferredPort)
    {
        await StopAsync().ConfigureAwait(false);
        await StartAsync(preferredPort).ConfigureAwait(false);
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
                try { await Task.Delay(100, token).ConfigureAwait(false); }
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
                await RespondTextAsync(stream, "text/html; charset=utf-8", BuildHtml(uri), token).ConfigureAwait(false);
                return;
            }

            if (path is "/settings" or "/overlay/settings")
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
                    source = t.Source,
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
                await RespondBytesAsync(stream, cover.ContentType, cover.Data, token, cacheSeconds: 86400).ConfigureAwait(false);
                return;
            }

            if (path == "/api/control")
            {
                var action = GetQuery(uri, "action") ?? "toggle";
                var ok = await _aimp.ControlAsync(action).ConfigureAwait(false);
                await RespondTextAsync(stream, "application/json; charset=utf-8", JsonSerializer.Serialize(new { ok, action }), token).ConfigureAwait(false);
                return;
            }

            if (path == "/health")
            {
                await RespondTextAsync(stream, "text/plain; charset=utf-8", "TiHiY AIMP Overlay OK", token).ConfigureAwait(false);
                return;
            }

            await RespondTextAsync(stream, "text/plain; charset=utf-8", "Not found", token, "404 Not Found").ConfigureAwait(false);
        }
    }

    private static string BuildHtml(Uri uri)
    {
        var width = ClampQuery(uri, "width", 620, 380, 1400);
        var height = ClampQuery(uri, "height", 112, 82, 260);
        var cover = ClampQuery(uri, "cover", 88, 54, 180);
        var backgroundOpacity = ClampQueryDouble(uri, "bg", 0.82, 0, 1);
        var widgetOpacity = ClampQueryDouble(uri, "opacity", 1, 0.15, 1);
        var radius = ClampQuery(uri, "radius", 11, 0, 40);

        return $$"""
<!doctype html><html lang="uk"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0;background:transparent;overflow:hidden;font-family:"Segoe UI",sans-serif;color:#f4f8ff}#card{position:absolute;left:8px;bottom:8px;width:min({{width}}px,calc(100vw - 16px));height:{{height}}px;box-sizing:border-box;padding:10px 12px;display:grid;grid-template-columns:{{cover}}px minmax(0,1fr);gap:12px;border-radius:{{radius}}px;background:linear-gradient(135deg,rgba(3,18,34,{{backgroundOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}}),rgba(5,38,64,{{Math.Max(0, backgroundOpacity - 0.04).ToString(System.Globalization.CultureInfo.InvariantCulture)}}));border:1px solid rgba(55,169,255,.52);border-left:4px solid #FFD329;box-shadow:0 8px 24px rgba(0,0,0,.28);opacity:0;transform:translateY(8px);transition:.22s;overflow:hidden}#card.on{opacity:{{widgetOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}};transform:none}.cover{width:{{cover}}px;height:{{cover}}px;align-self:center;border-radius:8px;object-fit:cover;background:#051221;border:1px solid rgba(255,211,41,.55)}.content{min-width:0;display:flex;flex-direction:column;justify-content:center}.top{display:flex;align-items:center;gap:8px}.label{font-size:10px;font-weight:900;letter-spacing:1.5px;color:#FFD329}.state{font-size:10px;font-weight:800;color:#66d8ff}.title{font-size:21px;line-height:1.12;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:2px}.artist{font-size:14px;line-height:1.15;font-weight:700;color:#bfe9ff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.album{font-size:10px;color:#8eaabb;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:1px}.timeline{display:grid;grid-template-columns:auto 1fr auto;gap:7px;align-items:center;margin-top:7px}.time{font:10px Consolas,monospace;color:#a9c2d2}.bar{height:4px;border-radius:999px;background:rgba(255,255,255,.14);overflow:hidden}.fill{height:100%;width:0;background:linear-gradient(90deg,#24a7ff,#FFD329);transition:width .2s linear}
</style></head><body><div id="card"><img id="cover" class="cover" alt="cover"><div class="content"><div class="top"><span class="label">AIMP • ЗАРАЗ ГРАЄ</span><span id="state" class="state"></span></div><div id="title" class="title"></div><div id="artist" class="artist"></div><div id="album" class="album"></div><div class="timeline"><span id="pos" class="time">0:00</span><div class="bar"><div id="fill" class="fill"></div></div><span id="dur" class="time">0:00</span></div></div></div><script>
let model=null,lastFetch=0,lastCover=-1;const $=id=>document.getElementById(id);function fmt(v){v=Math.max(0,Number(v)||0);const m=Math.floor(v/60),s=Math.floor(v%60);return m+':'+String(s).padStart(2,'0')}function renderProgress(){if(!model)return;let p=Number(model.positionSeconds)||0;if(model.isPlaying)p+=(performance.now()-lastFetch)/1000;const d=Number(model.durationSeconds)||0;if(d>0)p=Math.min(p,d);$('pos').textContent=fmt(p);$('dur').textContent=fmt(d);$('fill').style.width=(d>0?Math.max(0,Math.min(100,p/d*100)):0)+'%';requestAnimationFrame(renderProgress)}async function update(){try{const m=await(await fetch('/api/now-playing',{cache:'no-store'})).json();model=m;lastFetch=performance.now();$('card').classList.toggle('on',!!m.active);$('title').textContent=m.title||'';$('artist').textContent=m.artist||'';$('album').textContent=m.album||'';$('state').textContent=m.isPlaying?'PLAY':'PAUSE';if(Number(m.coverVersion)!==lastCover){lastCover=Number(m.coverVersion)||0;$('cover').src='/api/cover?v='+lastCover}}catch(e){}}setInterval(update,1000);update();requestAnimationFrame(renderProgress);
</script></body></html>
""";
    }

    private string BuildSettingsHtml() => $$"""
<!doctype html><html lang="uk"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
*{box-sizing:border-box}body{margin:0;padding:20px;background:#06172a;color:#eef7ff;font:14px "Segoe UI",sans-serif}.panel{max-width:980px;margin:auto;background:#09243c;border:1px solid #168dd0;border-left:5px solid #ffd329;border-radius:12px;padding:18px}h1{margin:0 0 6px;color:#ffd329;font-size:24px}.sub{color:#91aabd;margin-bottom:18px}.grid{display:grid;grid-template-columns:220px 1fr 70px;gap:12px;align-items:center;margin:12px 0}input[type=range]{width:100%}.value{font-weight:800;text-align:right;color:#67d8ff}.preview{height:230px;background:repeating-linear-gradient(45deg,#1a1a1a,#1a1a1a 12px,#252525 12px,#252525 24px);border:1px solid #36546a;border-radius:8px;overflow:hidden;margin-top:18px}.preview iframe{width:100%;height:100%;border:0}.url{width:100%;margin-top:16px;padding:11px;background:#03111e;color:#dff7ff;border:1px solid #168dd0;border-radius:6px}button{margin-top:10px;padding:10px 16px;border:1px solid #ffd329;border-radius:6px;background:#123b5c;color:white;font-weight:800;cursor:pointer}.note{margin-top:10px;color:#8fa9bb;font-size:12px}
</style></head><body><div class="panel"><h1>AIMP NOW PLAYING — НАЛАШТУВАННЯ</h1><div class="sub">Зміни параметри, скопіюй готовий URL і встав його в OBS Browser Source.</div>
<div class="grid"><label>Прозорість фону</label><input id="bg" type="range" min="0" max="100" value="82"><span id="bgv" class="value">82%</span></div>
<div class="grid"><label>Прозорість віджета</label><input id="op" type="range" min="15" max="100" value="100"><span id="opv" class="value">100%</span></div>
<div class="grid"><label>Висота</label><input id="h" type="range" min="82" max="220" value="112"><span id="hv" class="value">112 px</span></div>
<div class="grid"><label>Ширина</label><input id="w" type="range" min="380" max="1200" value="620"><span id="wv" class="value">620 px</span></div>
<div class="grid"><label>Обкладинка</label><input id="c" type="range" min="54" max="160" value="88"><span id="cv" class="value">88 px</span></div>
<div class="preview"><iframe id="frame"></iframe></div><input id="url" class="url" readonly><button onclick="copyUrl()">СКОПІЮВАТИ URL ДЛЯ OBS</button><div class="note">Рекомендована висота Browser Source в OBS: 140–180 px. Фон сторінки прозорий.</div></div><script>
const ids=['bg','op','h','w','c'];ids.forEach(id=>document.getElementById(id).addEventListener('input',update));function update(){const bg=+bgEl.value,op=+opEl.value,h=+hEl.value,w=+wEl.value,c=+cEl.value;bgv.textContent=bg+'%';opv.textContent=op+'%';hv.textContent=h+' px';wv.textContent=w+' px';cv.textContent=c+' px';const u='http://127.0.0.1:{{Port}}/overlay/now-playing?bg='+(bg/100).toFixed(2)+'&opacity='+(op/100).toFixed(2)+'&height='+h+'&width='+w+'&cover='+c;url.value=u;frame.src=u+'&t='+Date.now()}const bgEl=document.getElementById('bg'),opEl=document.getElementById('op'),hEl=document.getElementById('h'),wEl=document.getElementById('w'),cEl=document.getElementById('c');async function copyUrl(){await navigator.clipboard.writeText(url.value)}update();
</script></body></html>
""";

    private static string? GetQuery(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static int ClampQuery(Uri uri, string name, int fallback, int min, int max) =>
        int.TryParse(GetQuery(uri, name), out var value) ? Math.Clamp(value, min, max) : fallback;

    private static double ClampQueryDouble(Uri uri, string name, double fallback, double min, double max) =>
        double.TryParse(GetQuery(uri, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

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

    private const string PlaceholderSvg = """
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256"><rect width="256" height="256" rx="20" fill="#06182b"/><path d="M162 48v112.5a42 42 0 1 1-18-34V75l-70 15v86.5a42 42 0 1 1-18-34V76z" fill="#FFD329" opacity=".9"/></svg>
""";

    public async Task StopAsync()
    {
        if (_listener is null) return;
        _cts?.Cancel();
        _listener.Stop();
        _listener = null;
        if (_loop is not null)
            try { await _loop.ConfigureAwait(false); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
