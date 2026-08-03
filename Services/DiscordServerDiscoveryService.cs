using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Services;

public static class DiscordServerDiscoveryService
{
    private const ulong Administrator = 1UL << 3;
    private const ulong ViewChannel = 1UL << 10;
    private const ulong SendMessages = 1UL << 11;
    private const ulong EmbedLinks = 1UL << 14;
    private const ulong MentionEveryone = 1UL << 17;

    public static async Task<IReadOnlyList<DiscordServerChannelInfo>> DiscoverAsync(
        string botToken,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            throw new InvalidOperationException("Discord Bot Token не збережено.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), timeout.Token);

        var hello = await ReceiveJsonAsync(socket, timeout.Token);
        if (hello?["op"]?.GetValue<int>() != 10)
            throw new InvalidOperationException("Discord Gateway не повернув HELLO.");

        var identify = new JsonObject
        {
            ["op"] = 2,
            ["d"] = new JsonObject
            {
                ["token"] = botToken.Trim(),
                ["intents"] = 1,
                ["properties"] = new JsonObject
                {
                    ["os"] = "windows",
                    ["browser"] = "TiHiY StreamControl MINI",
                    ["device"] = "TiHiY StreamControl MINI"
                }
            }
        };
        await SendJsonAsync(socket, identify, timeout.Token);

        var expectedGuilds = new HashSet<string>(StringComparer.Ordinal);
        var receivedGuilds = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<DiscordServerChannelInfo>();
        string botUserId = string.Empty;
        var readyReceived = false;

        while (!timeout.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            JsonObject? payload;
            try { payload = await ReceiveJsonAsync(socket, timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
            if (payload is null) continue;

            var op = payload["op"]?.GetValue<int>() ?? -1;
            if (op == 9) throw new InvalidOperationException("Discord Gateway відхилив сесію бота. Перевірте Bot Token.");
            if (op == 7) break;
            if (op != 0) continue;

            var eventName = payload["t"]?.GetValue<string>() ?? string.Empty;
            var data = payload["d"] as JsonObject;
            if (data is null) continue;

            if (eventName.Equals("READY", StringComparison.OrdinalIgnoreCase))
            {
                readyReceived = true;
                botUserId = data["user"]?["id"]?.GetValue<string>() ?? string.Empty;
                if (data["guilds"] is JsonArray guilds)
                {
                    foreach (var guild in guilds.OfType<JsonObject>())
                    {
                        var id = guild["id"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(id)) expectedGuilds.Add(id);
                    }
                }
                if (expectedGuilds.Count == 0) break;
                continue;
            }

            if (!eventName.Equals("GUILD_CREATE", StringComparison.OrdinalIgnoreCase)) continue;
            var guildId = data["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(guildId)) continue;
            if (data["unavailable"]?.GetValue<bool?>() == true) continue;

            receivedGuilds.Add(guildId);
            rows.AddRange(ParseGuild(data, botUserId));

            if (readyReceived && expectedGuilds.Count > 0 && expectedGuilds.All(receivedGuilds.Contains))
                break;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Discovery complete", CancellationToken.None);
        }
        catch { }

        return rows
            .Where(x => x.IsNotificationChannel)
            .OrderBy(x => x.ServerName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Position)
            .ThenBy(x => x.ChannelName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<DiscordServerChannelInfo> ParseGuild(JsonObject guild, string botUserId)
    {
        var guildId = guild["id"]?.GetValue<string>() ?? string.Empty;
        var guildName = guild["name"]?.GetValue<string>() ?? guildId;
        if (string.IsNullOrWhiteSpace(guildId)) yield break;

        var roles = (guild["roles"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var botMember = FindBotMember(guild["members"] as JsonArray, botUserId);
        var memberRoleIds = new HashSet<string>(StringComparer.Ordinal);
        if (botMember?["roles"] is JsonArray memberRoles)
            foreach (var role in memberRoles)
                if (role is not null) memberRoleIds.Add(role.GetValue<string>());

        var basePermissions = PermissionOfRole(roles.FirstOrDefault(x => string.Equals(x["id"]?.GetValue<string>(), guildId, StringComparison.Ordinal)));
        foreach (var role in roles)
        {
            var id = role["id"]?.GetValue<string>() ?? string.Empty;
            if (memberRoleIds.Contains(id)) basePermissions |= PermissionOfRole(role);
        }

        var channels = (guild["channels"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var categories = channels
            .Where(x => (x["type"]?.GetValue<int>() ?? -1) == 4)
            .ToDictionary(x => x["id"]?.GetValue<string>() ?? string.Empty, x => x["name"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal);

        foreach (var channel in channels)
        {
            var type = channel["type"]?.GetValue<int>() ?? -1;
            if (type is not (0 or 5)) continue;
            var permissions = ApplyOverwrites(basePermissions, channel["permission_overwrites"] as JsonArray, guildId, botUserId, memberRoleIds);
            var parentId = channel["parent_id"]?.GetValue<string>();
            categories.TryGetValue(parentId ?? string.Empty, out var categoryName);

            yield return new DiscordServerChannelInfo
            {
                ServerId = guildId,
                ServerName = guildName,
                ChannelId = channel["id"]?.GetValue<string>() ?? string.Empty,
                ChannelName = channel["name"]?.GetValue<string>() ?? "канал",
                CategoryName = categoryName ?? string.Empty,
                ChannelType = type,
                Position = channel["position"]?.GetValue<int>() ?? 0,
                CanView = Has(permissions, ViewChannel),
                CanSend = Has(permissions, SendMessages),
                CanEmbedLinks = Has(permissions, EmbedLinks),
                CanMentionEveryone = Has(permissions, MentionEveryone)
            };
        }
    }

    private static JsonObject? FindBotMember(JsonArray? members, string botUserId)
    {
        if (members is null || string.IsNullOrWhiteSpace(botUserId)) return null;
        return members.OfType<JsonObject>().FirstOrDefault(x =>
            string.Equals(x["user"]?["id"]?.GetValue<string>(), botUserId, StringComparison.Ordinal));
    }

    private static ulong ApplyOverwrites(
        ulong permissions,
        JsonArray? overwrites,
        string guildId,
        string botUserId,
        HashSet<string> memberRoleIds)
    {
        if (Has(permissions, Administrator)) return ulong.MaxValue;
        if (overwrites is null) return permissions;

        var items = overwrites.OfType<JsonObject>().ToList();
        var everyone = items.FirstOrDefault(x =>
            (x["type"]?.GetValue<int>() ?? -1) == 0 &&
            string.Equals(x["id"]?.GetValue<string>(), guildId, StringComparison.Ordinal));
        Apply(ref permissions, everyone);

        ulong roleAllow = 0;
        ulong roleDeny = 0;
        foreach (var item in items)
        {
            if ((item["type"]?.GetValue<int>() ?? -1) != 0) continue;
            var id = item["id"]?.GetValue<string>() ?? string.Empty;
            if (!memberRoleIds.Contains(id)) continue;
            roleAllow |= ParsePermission(item["allow"]?.GetValue<string>());
            roleDeny |= ParsePermission(item["deny"]?.GetValue<string>());
        }
        permissions &= ~roleDeny;
        permissions |= roleAllow;

        var member = items.FirstOrDefault(x =>
            (x["type"]?.GetValue<int>() ?? -1) == 1 &&
            string.Equals(x["id"]?.GetValue<string>(), botUserId, StringComparison.Ordinal));
        Apply(ref permissions, member);
        return permissions;
    }

    private static void Apply(ref ulong permissions, JsonObject? overwrite)
    {
        if (overwrite is null) return;
        var deny = ParsePermission(overwrite["deny"]?.GetValue<string>());
        var allow = ParsePermission(overwrite["allow"]?.GetValue<string>());
        permissions &= ~deny;
        permissions |= allow;
    }

    private static ulong PermissionOfRole(JsonObject? role) => ParsePermission(role?["permissions"]?.GetValue<string>());
    private static ulong ParsePermission(string? value) => ulong.TryParse(value, out var result) ? result : 0;
    private static bool Has(ulong permissions, ulong flag) => (permissions & flag) == flag;

    private static async Task SendJsonAsync(ClientWebSocket socket, JsonObject payload, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private static async Task<JsonObject?> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[32768];
        var text = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        return JsonNode.Parse(text.ToString()) as JsonObject;
    }
}
