using System.Collections.Specialized;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocketIOClient;
using SocketIOClient.Common;
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
    private SocketIO? _socket;

    public bool IsAuthorized => !string.IsNullOrWhiteSpace(_credentials.LoadSecret(AccessTokenKey));
    public bool IsConnected => _socket?.Connected == true;
    public string Status { get; private set; } = "НЕ ПІДКЛЮЧЕНО";
    public event EventHandler? StatusChanged;
    public event EventHandler<StreamlabsEvent>? EventReceived;

    public StreamlabsService(LitePreferencesStore prefs, CredentialService credentials, AppLogger logger)
    {
        _prefs = prefs;
        _credentials = credentials;
        _logger = logger;
    }

    public void SaveClientSecret(string secret)
    {
        if (!string.IsNullOrWhiteSpace(secret)) _credentials.SaveSecret(SecretKey, secret.Trim());
    }

    public void ForgetAuthorization()
    {
        _credentials.DeleteSecret(AccessTokenKey);
        SetStatus("АВТОРИЗАЦІЮ ВИДАЛЕНО");
    }

    public async Task AuthorizeAsync(string clientId, string clientSecret, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Вкажіть Streamlabs Client ID.");
        if (string.IsNullOrWhiteSpace(clientSecret)) clientSecret = _credentials.LoadSecret(SecretKey);
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new InvalidOperationException("Вкажіть Streamlabs Client Secret.");
        _prefs.Value.StreamlabsClientId = clientId.Trim();
        _prefs.Save();
        SaveClientSecret(clientSecret);
        var state = Guid.NewGuid().ToString("N");
        var scopes = "socket.token donations.read alerts.create alerts.write profiles.write";
        var url = "https://streamlabs.com/api/v2.0/authorize" +
                  $"?response_type=code&client_id={Uri.EscapeDataString(clientId.Trim())}" +
                  $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                  $"&scope={Uri.EscapeDataString(scopes)}&state={state}";
        SetStatus("ОЧІКУЮ БРАУЗЕР");
        var result = await OAuthLoopback.AuthorizeAsync(url, RedirectUri, token).ConfigureAwait(false);
        if (result.TryGetValue("error", out var error)) throw new InvalidOperationException("Streamlabs OAuth: " + error);
        if (!result.TryGetValue("state", out var returnedState) || !string.Equals(returnedState, state, StringComparison.Ordinal)) throw new InvalidOperationException("Streamlabs OAuth: невірний state.");
        if (!result.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Streamlabs не повернув код авторизації.");
        var tokenPayload = new JsonObject { ["grant_type"]="authorization_code",["client_id"]=clientId.Trim(),["client_secret"]=clientSecret.Trim(),["redirect_uri"]=RedirectUri,["code"]=code };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://streamlabs.com/api/v2.0/token");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Content = new StringContent(tokenPayload.ToJsonString(), Encoding.UTF8, "application/json");
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
        var socketToken = node?["socket_token"]?.GetValue<string>() ?? node?["token"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(socketToken)) throw new InvalidOperationException("Streamlabs не повернув socket token.");
        var query = new NameValueCollection { ["token"] = socketToken };
        _socket = new SocketIO(new Uri("https://sockets.streamlabs.com"), new SocketIOOptions
        {
            Query = query,
            EIO = EngineIO.V3,
            Transport = TransportProtocol.WebSocket,
            AutoUpgrade = false,
            Reconnection = true,
            ReconnectionAttempts = 20,
            ReconnectionDelayMax = 5000,
            ConnectionTimeout = TimeSpan.FromSeconds(15)
        });
        _socket.On("event", ctx =>
        {
            try { ParseSocketEvent(ctx.RawText); }
            catch (Exception ex) { _logger.Error("Streamlabs event parse", ex); }
            return Task.CompletedTask;
        });
        await _socket.ConnectAsync(token).ConfigureAwait(false);
        SetStatus("ПІДКЛЮЧЕНО");
    }

    private void ParseSocketEvent(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        JsonElement data;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2) data = root[1];
        else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1) data = root[0];
        else data = root;
        if (data.ValueKind != JsonValueKind.Object) return;
        var type = GetString(data, "type");
        var platform = GetString(data, "for");
        var eventId = GetString(data, "event_id");
        if (!data.TryGetProperty("message", out var messages)) return;
        if (messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in messages.EnumerateArray()) EventReceived?.Invoke(this, new StreamlabsEvent { For=platform,Type=type,EventId=eventId,Payload=item.Clone() });
        }
        else EventReceived?.Invoke(this, new StreamlabsEvent { For=platform,Type=type,EventId=eventId,Payload=messages.Clone() });
    }

    public async Task<IReadOnlyList<JsonObject>> GetRecentDonationsAsync(int limit = 20, CancellationToken token = default)
    {
        var access = _credentials.LoadSecret(AccessTokenKey);
        if (string.IsNullOrWhiteSpace(access)) return Array.Empty<JsonObject>();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://streamlabs.com/api/v2.0/donations?limit={Math.Clamp(limit,1,100)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return Array.Empty<JsonObject>();
        var root = JsonNode.Parse(body);
        var array = root?["data"] as JsonArray ?? root as JsonArray;
        return array is null ? Array.Empty<JsonObject>() : array.OfType<JsonObject>().ToList();
    }

    public Task SendTestAlertAsync(string type="follow",CancellationToken token=default)=>PostAlertCommandAsync("alerts/send_test_alert",new Dictionary<string,string>{{"type",type}},token);
    public Task SkipAlertAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/skip",null,token);
    public Task PauseAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/pause_queue",null,token);
    public Task ResumeAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/unpause_queue",null,token);
    public Task MuteAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/mute_volume",null,token);
    public Task UnmuteAlertsAsync(CancellationToken token=default)=>PostAlertCommandAsync("alerts/unmute_volume",null,token);

    private async Task PostAlertCommandAsync(string endpoint,Dictionary<string,string>? parameters,CancellationToken token)
    {
        var access=_credentials.LoadSecret(AccessTokenKey); if(string.IsNullOrWhiteSpace(access))throw new InvalidOperationException("Streamlabs не авторизовано.");
        var url="https://streamlabs.com/api/v2.0/"+endpoint;
        if(parameters is {Count:>0})url+="?"+string.Join("&",parameters.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
        using var req=new HttpRequestMessage(HttpMethod.Post,url); req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        using var resp=await _http.SendAsync(req,token).ConfigureAwait(false); var body=await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if(!resp.IsSuccessStatusCode)throw new InvalidOperationException($"Streamlabs {(int)resp.StatusCode}: {body}");
    }

    public static void OpenDeveloperPage()=>Process.Start(new ProcessStartInfo("https://streamlabs.com/dashboard#/settings/api-settings"){UseShellExecute=true});
    private static string GetString(JsonElement element,string name){if(!element.TryGetProperty(name,out var value))return string.Empty;return value.ValueKind==JsonValueKind.String?value.GetString()??string.Empty:value.ToString();}
    private void SetStatus(string value){Status=value;StatusChanged?.Invoke(this,EventArgs.Empty);_logger.Info("Streamlabs: "+value);}
    public async Task DisconnectAsync(){if(_socket is null)return;try{if(_socket.Connected)await _socket.DisconnectAsync().ConfigureAwait(false);}catch{} _socket.Dispose();_socket=null;SetStatus("НЕ ПІДКЛЮЧЕНО");}
    public async ValueTask DisposeAsync(){await DisconnectAsync().ConfigureAwait(false);_http.Dispose();}
}
