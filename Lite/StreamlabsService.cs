using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public sealed class StreamlabsEvent
{
    public string For { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string EventId { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
}

public sealed class StreamlabsService : IAsyncDisposable
{
    private const string RedirectUri = "http://127.0.0.1:17848/streamlabs/";
    private const string AccessTokenKey = "STREAMLABS_ACCESS_TOKEN";
    private const string SecretKey = "STREAMLABS_CLIENT_SECRET";
    private readonly LitePreferencesStore _prefs;
    private readonly CredentialService _credentials;
    private readonly AppLogger _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _socketCts;
    private Task? _socketLoop;
    private string _socketToken = string.Empty;

    public bool IsAuthorized => !string.IsNullOrWhiteSpace(_credentials.LoadSecret(AccessTokenKey));
    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public string Status { get; private set; } = "НЕ ПІДКЛЮЧЕНО";
    public event EventHandler? StatusChanged;
    public event EventHandler<StreamlabsEvent>? EventReceived;

    public StreamlabsService(LitePreferencesStore prefs, CredentialService credentials, AppLogger logger)
    {
        _prefs = prefs; _credentials = credentials; _logger = logger;
    }

    public void SaveClientSecret(string secret) { if (!string.IsNullOrWhiteSpace(secret)) _credentials.SaveSecret(SecretKey, secret.Trim()); }
    public void ForgetAuthorization() { _credentials.DeleteSecret(AccessTokenKey); SetStatus("АВТОРИЗАЦІЮ ВИДАЛЕНО"); }

    public async Task AuthorizeAsync(string clientId, string clientSecret, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Вкажіть Streamlabs Client ID.");
        if (string.IsNullOrWhiteSpace(clientSecret)) clientSecret = _credentials.LoadSecret(SecretKey);
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new InvalidOperationException("Вкажіть Streamlabs Client Secret.");
        _prefs.Value.StreamlabsClientId = clientId.Trim(); _prefs.Save(); SaveClientSecret(clientSecret);
        var state = Guid.NewGuid().ToString("N");
        var scopes = "socket.token donations.read alerts.create alerts.write profiles.write";
        var url = "https://streamlabs.com/api/v2.0/authorize" +
                  $"?response_type=code&client_id={Uri.EscapeDataString(clientId.Trim())}" +
                  $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope={Uri.EscapeDataString(scopes)}&state={state}";
        SetStatus("ОЧІКУЮ БРАУЗЕР");
        var result = await OAuthLoopback.AuthorizeAsync(url, RedirectUri, token).ConfigureAwait(false);
        if (result.TryGetValue("error", out var error)) throw new InvalidOperationException("Streamlabs OAuth: " + error);
        if (!result.TryGetValue("state", out var returnedState) || returnedState != state) throw new InvalidOperationException("Streamlabs OAuth: невірний state.");
        if (!result.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Streamlabs не повернув код авторизації.");
        var payload = new JsonObject { ["grant_type"]="authorization_code",["client_id"]=clientId.Trim(),["client_secret"]=clientSecret.Trim(),["redirect_uri"]=RedirectUri,["code"]=code };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://streamlabs.com/api/v2.0/token");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Streamlabs token {(int)response.StatusCode}: {body}");
        var json = JsonNode.Parse(body)?.AsObject() ?? new JsonObject();
        var access = json["access_token"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Streamlabs не повернув access token.");
        _credentials.SaveSecret(AccessTokenKey, access);
        await ConnectAsync(token).ConfigureAwait(false);
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        var access = _credentials.LoadSecret(AccessTokenKey);
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Streamlabs не авторизовано.");
        await DisconnectAsync().ConfigureAwait(false);
        SetStatus("ПІДКЛЮЧЕННЯ");
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://streamlabs.com/api/v2.0/socket/token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Streamlabs socket token {(int)resp.StatusCode}: {body}");
        var node = JsonNode.Parse(body);
        _socketToken = node?["socket_token"]?.GetValue<string>() ?? node?["token"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_socketToken)) throw new InvalidOperationException("Streamlabs не повернув socket token.");
        _socketCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        await ConnectSocketOnceAsync(_socketCts.Token).ConfigureAwait(false);
        _socketLoop = Task.Run(() => MaintainSocketAsync(_socketCts.Token));
    }

    private async Task ConnectSocketOnceAsync(CancellationToken token)
    {
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var uri = new Uri("wss://sockets.streamlabs.com/socket.io/?EIO=3&transport=websocket&token=" + Uri.EscapeDataString(_socketToken));
        await _socket.ConnectAsync(uri, token).ConfigureAwait(false);
        SetStatus("ПІДКЛЮЧЕНО");
    }

    private async Task MaintainSocketAsync(CancellationToken token)
    {
        var retry = 1000;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_socket is null || _socket.State != WebSocketState.Open) await ConnectSocketOnceAsync(token).ConfigureAwait(false);
                await ReceiveLoopAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.Error("Streamlabs socket", ex); }
            if (token.IsCancellationRequested) break;
            SetStatus("ПЕРЕПІДКЛЮЧЕННЯ");
            try { await Task.Delay(retry, token).ConfigureAwait(false); } catch { break; }
            retry = Math.Min(5000, retry + 750);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[16384];
        var text = new StringBuilder();
        while (!token.IsCancellationRequested && _socket?.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                text.Append(Encoding.UTF8.GetString(buffer,0,result.Count));
            } while (!result.EndOfMessage);
            var frame = text.ToString(); text.Clear();
            if (frame == "2") { await SendSocketTextAsync("3", token).ConfigureAwait(false); continue; }
            if (frame.StartsWith("0", StringComparison.Ordinal)) { await SendSocketTextAsync("40", token).ConfigureAwait(false); continue; }
            if (frame.StartsWith("42", StringComparison.Ordinal) && frame.Length > 2) ParseSocketPacket(frame[2..]);
        }
    }

    private async Task SendSocketTextAsync(string text, CancellationToken token)
    {
        if (_socket?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
    }

    private void ParseSocketPacket(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2) return;
        if (!string.Equals(root[0].GetString(), "event", StringComparison.OrdinalIgnoreCase)) return;
        var data = root[1]; if (data.ValueKind != JsonValueKind.Object) return;
        var type = GetString(data,"type"); var platform = GetString(data,"for"); var eventId = GetString(data,"event_id");
        if (!data.TryGetProperty("message",out var messages)) return;
        if (messages.ValueKind == JsonValueKind.Array)
            foreach (var item in messages.EnumerateArray()) EventReceived?.Invoke(this,new StreamlabsEvent{For=platform,Type=type,EventId=eventId,Payload=item.Clone()});
        else EventReceived?.Invoke(this,new StreamlabsEvent{For=platform,Type=type,EventId=eventId,Payload=messages.Clone()});
    }

    public async Task<IReadOnlyList<JsonObject>> GetRecentDonationsAsync(int limit=20,CancellationToken token=default)
    {
        var access=_credentials.LoadSecret(AccessTokenKey); if(string.IsNullOrWhiteSpace(access))return Array.Empty<JsonObject>();
        using var req=new HttpRequestMessage(HttpMethod.Get,$"https://streamlabs.com/api/v2.0/donations?limit={Math.Clamp(limit,1,100)}"); req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        using var resp=await _http.SendAsync(req,token).ConfigureAwait(false); var body=await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false); if(!resp.IsSuccessStatusCode)return Array.Empty<JsonObject>();
        var root=JsonNode.Parse(body); var array=root?["data"] as JsonArray??root as JsonArray; return array is null?Array.Empty<JsonObject>():array.OfType<JsonObject>().ToList();
    }

    public Task SendTestAlertAsync(string type="follow",CancellationToken token=default)=>PostAlertCommandAsync("alerts/send_test_alert",new Dictionary<string,string>{{"type",type},{"platform","twitch"}},token);
    public Task SkipAlertAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/skip",null,token);
    public Task PauseAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/pause_queue",null,token);
    public Task ResumeAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/unpause_queue",null,token);
    public Task MuteAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/mute_volume",null,token);
    public Task UnmuteAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/unmute_volume",null,token);

    private async Task PostAlertCommandAsync(string endpoint,Dictionary<string,string>? parameters,CancellationToken token)
    {
        var access=_credentials.LoadSecret(AccessTokenKey); if(string.IsNullOrWhiteSpace(access))throw new InvalidOperationException("Streamlabs не авторизовано.");
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://streamlabs.com/api/v2.0/"+endpoint); req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        if(parameters is {Count:>0})req.Content=new FormUrlEncodedContent(parameters);
        using var resp=await _http.SendAsync(req,token).ConfigureAwait(false); var body=await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false); if(!resp.IsSuccessStatusCode)throw new InvalidOperationException($"Streamlabs {(int)resp.StatusCode}: {body}");
    }

    public static void OpenDeveloperPage()=>Process.Start(new ProcessStartInfo("https://streamlabs.com/dashboard#/settings/api-settings"){UseShellExecute=true});
    private static string GetString(JsonElement element,string name){if(!element.TryGetProperty(name,out var value))return string.Empty;return value.ValueKind==JsonValueKind.String?value.GetString()??string.Empty:value.ToString();}
    private void SetStatus(string value){Status=value;StatusChanged?.Invoke(this,EventArgs.Empty);_logger.Info("Streamlabs: "+value);}

    public async Task DisconnectAsync()
    {
        _socketCts?.Cancel();
        if(_socket is not null)
        {
            try{if(_socket.State==WebSocketState.Open)await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure,"closing",CancellationToken.None).ConfigureAwait(false);}catch{}
            _socket.Dispose(); _socket=null;
        }
        if(_socketLoop is not null)try{await _socketLoop.ConfigureAwait(false);}catch{}
        _socketLoop=null; _socketCts?.Dispose(); _socketCts=null; SetStatus("НЕ ПІДКЛЮЧЕНО");
    }
    public async ValueTask DisposeAsync(){await DisconnectAsync().ConfigureAwait(false);_http.Dispose();}
}
