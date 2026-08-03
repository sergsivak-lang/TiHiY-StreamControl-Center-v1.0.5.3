using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// Tiny dedicated server for the full AIMP widget. It is idle when OBS is not requesting it,
/// uses cached event-driven AIMP state, and never polls AIMP in the background.
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
                await RespondTextAsync(stream, "text/html; charset=utf-8", BuildHtml(), token).ConfigureAwait(false);
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

    private static string BuildHtml() => """
<!doctype html><html lang="uk"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
html,body{margin:0;background:transparent;overflow:hidden;font-family:"Segoe UI",sans-serif;color:#f4f8ff}#card{position:absolute;left:18px;bottom:18px;width:min(720px,calc(100vw - 36px));min-height:152px;box-sizing:border-box;padding:14px 16px;display:grid;grid-template-columns:128px minmax(0,1fr);gap:16px;border-radius:12px;background:linear-gradient(135deg,rgba(3,18,34,.94),rgba(5,38,64,.90));border:1px solid rgba(55,169,255,.58);border-left:5px solid #FFD329;box-shadow:0 12px 35px rgba(0,0,0,.35);opacity:0;transform:translateY(14px);transition:.25s}#card.on{opacity:1;transform:none}.cover{width:128px;height:128px;border-radius:9px;object-fit:cover;background:#051221;border:1px solid rgba(255,211,41,.6)}.content{min-width:0;display:flex;flex-direction:column;justify-content:center}.top{display:flex;align-items:center;gap:8px}.label{font-size:11px;font-weight:900;letter-spacing:1.8px;color:#FFD329}.state{font-size:11px;font-weight:800;color:#66d8ff}.title{font-size:25px;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:4px}.artist{font-size:17px;font-weight:700;color:#bfe9ff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.album{font-size:12px;color:#8eaabb;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:2px}.timeline{display:grid;grid-template-columns:auto 1fr auto;gap:8px;align-items:center;margin-top:10px}.time{font:11px Consolas,monospace;color:#a9c2d2}.bar{height:5px;border-radius:999px;background:rgba(255,255,255,.14);overflow:hidden}.fill{height:100%;width:0;background:linear-gradient(90deg,#24a7ff,#FFD329);transition:width .2s linear}.controls{display:flex;gap:6px;margin-top:9px}.ctrl{width:36px;height:28px;border:1px solid rgba(77,185,255,.55);border-radius:5px;background:rgba(4,35,61,.88);color:#fff;font-size:14px;cursor:pointer}.ctrl:hover{border-color:#FFD329;color:#FFD329}.hint{font-size:10px;color:#6f8a9b;margin-left:6px;align-self:center}
</style></head><body><div id="card"><img id="cover" class="cover" alt="cover"><div class="content"><div class="top"><span class="label">AIMP • ЗАРАЗ ГРАЄ</span><span id="state" class="state"></span></div><div id="title" class="title"></div><div id="artist" class="artist"></div><div id="album" class="album"></div><div class="timeline"><span id="pos" class="time">0:00</span><div class="bar"><div id="fill" class="fill"></div></div><span id="dur" class="time">0:00</span></div><div class="controls"><button class="ctrl" onclick="control('previous')" title="Попередній">◀◀</button><button id="toggle" class="ctrl" onclick="control('toggle')" title="Play / Pause">▶</button><button class="ctrl" onclick="control('next')" title="Наступний">▶▶</button><span class="hint">Керування доступне через OBS → Interact</span></div></div></div><script>
let model=null,lastFetch=0,lastCover=-1;const $=id=>document.getElementById(id);function fmt(v){v=Math.max(0,Number(v)||0);const m=Math.floor(v/60),s=Math.floor(v%60);return m+':'+String(s).padStart(2,'0')}function renderProgress(){if(!model)return;let p=Number(model.positionSeconds)||0;if(model.isPlaying)p+=(performance.now()-lastFetch)/1000;const d=Number(model.durationSeconds)||0;if(d>0)p=Math.min(p,d);$('pos').textContent=fmt(p);$('dur').textContent=fmt(d);$('fill').style.width=(d>0?Math.max(0,Math.min(100,p/d*100)):0)+'%';requestAnimationFrame(renderProgress)}async function update(){try{const m=await(await fetch('/api/now-playing',{cache:'no-store'})).json();model=m;lastFetch=performance.now();$('card').classList.toggle('on',!!m.active);$('title').textContent=m.title||'';$('artist').textContent=m.artist||'';$('album').textContent=m.album||'';$('state').textContent=m.isPlaying?'PLAY':'PAUSE';$('toggle').textContent=m.isPlaying?'Ⅱ':'▶';if(Number(m.coverVersion)!==lastCover){lastCover=Number(m.coverVersion)||0;$('cover').src='/api/cover?v='+lastCover}}catch(e){}}async function control(action){try{await fetch('/api/control?action='+encodeURIComponent(action),{cache:'no-store'});setTimeout(update,120)}catch(e){}}setInterval(update,1000);update();requestAnimationFrame(renderProgress);
</script></body></html>
""";

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
