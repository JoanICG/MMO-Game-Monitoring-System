using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Backend;

public class GameSession
{
    private readonly ConcurrentDictionary<Guid, (PlayerState state, WebSocket socket)> _players = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _broadcastLock = new object();
    private DateTime _lastBroadcast = DateTime.MinValue;
    private const int BROADCAST_THROTTLE_MS = 50; // Max 20 broadcasts per second

    public async Task HandleAsync(WebSocket socket, bool isAdmin = false)
    {
        PlayerState? me = null;
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            }
            catch { break; }

            if (result.MessageType == WebSocketMessageType.Close) break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"[GameSession] RX: {json}");
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("op", out var opProp)) continue;
                var op = opProp.GetString();
                switch (op)
                {
                    case "join":
                        var name = doc.RootElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Player" : "Player";
                        me = new PlayerState { Name = name[..Math.Min(name.Length, 16)] };
                        _players[me.Id] = (me, socket);
                        Console.WriteLine($"[GameSession] JOIN id={me.Id} name={me.Name} total={_players.Count}");
                        await SendAsync(socket, new JoinAck("join_ack", me.Id));
                        await BroadcastStateThrottled();
                        break;
                    case "admin_join":
                        if (!isAdmin) break;
                        var adminName = doc.RootElement.TryGetProperty("name", out var adminNameProp) ? adminNameProp.GetString() ?? "Admin" : "Admin";
                        me = new PlayerState { Name = adminName[..Math.Min(adminName.Length, 16)], IsAdmin = true };
                        _players[me.Id] = (me, socket);
                        Console.WriteLine($"[GameSession] ADMIN JOIN id={me.Id} name={me.Name} total={_players.Count}");
                        await SendAsync(socket, new JoinAck("join_ack", me.Id));
                        await BroadcastStateThrottled();
                        break;
                    case "move":
                        if (me == null || me.IsAdmin) break;
                        
                        // SERVER AUTHORITY: Ignore moves during server authority period
                        if (me.ServerAuthorityUntil.HasValue && DateTime.UtcNow < me.ServerAuthorityUntil.Value)
                        {
                            Console.WriteLine($"[GameSession] MOVE IGNORED (SERVER AUTHORITY) id={me.Id} authority_until={me.ServerAuthorityUntil.Value:HH:mm:ss.fff}");
                            break;
                        }
                        
                        if (doc.RootElement.TryGetProperty("x", out var xP)) me.X = xP.GetSingle();
                        if (doc.RootElement.TryGetProperty("y", out var yP)) me.Y = yP.GetSingle();
                        if (doc.RootElement.TryGetProperty("z", out var zP)) me.Z = zP.GetSingle();
                        me.LastUpdate = DateTime.UtcNow;
                        Console.WriteLine($"[GameSession] MOVE ACCEPTED id={me.Id} pos=({me.X},{me.Y},{me.Z})");
                        await BroadcastStateThrottled();
                        break;
                    case "admin_spawn_npc":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminSpawnNPC(doc);
                        break;
                    case "admin_teleport":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminTeleport(doc);
                        break;
                    case "admin_kick":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminKick(doc);
                        break;
                    case "admin_teleport_all":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminTeleportAll(doc);
                        break;
                    case "admin_kick_all":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminKickAll();
                        break;
                }
            }
            catch { /* ignore malformed */ }
        }

        if (me != null)
        {
            _players.TryRemove(me.Id, out _);
            await BroadcastState(); // Force broadcast on disconnect
        }

        if (socket.State == WebSocketState.Open)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }

    private Task SendAsync(WebSocket socket, object payload)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private Task BroadcastStateThrottled()
    {
        lock (_broadcastLock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastBroadcast).TotalMilliseconds < BROADCAST_THROTTLE_MS)
            {
                return Task.CompletedTask; // Skip if too frequent
            }
            _lastBroadcast = now;
        }
        return BroadcastState();
    }

    private Task BroadcastState()
    {
        var snap = new StateSnapshot(
            "state",
            _players.Values.Select(p => new PlayerDto(p.state.Id, p.state.Name, p.state.X, p.state.Y, p.state.Z, p.state.IsNPC))
        );
        var json = JsonSerializer.Serialize(snap, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        Console.WriteLine($"[GameSession] BROADCAST players={_players.Count}");
        var tasks = _players.Values
            .Where(v => v.socket != null && v.socket.State == WebSocketState.Open)
            .Select(v => v.socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None));
        return Task.WhenAll(tasks);
    }

    private async Task HandleAdminSpawnNPC(JsonDocument doc)
    {
        var name = doc.RootElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "NPC" : "NPC";
        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        var npc = new PlayerState 
        { 
            Name = name[..Math.Min(name.Length, 16)], 
            X = x, 
            Y = y, 
            Z = z, 
            IsNPC = true 
        };
        
        // NPCs don't have real WebSocket, use null
        _players[npc.Id] = (npc, null!);
        Console.WriteLine($"[GameSession] ADMIN SPAWN NPC id={npc.Id} name={npc.Name} pos=({x},{y},{z})");
        await BroadcastState();
    }

    private async Task HandleAdminTeleport(JsonDocument doc)
    {
        var playerIdStr = doc.RootElement.TryGetProperty("playerId", out var playerIdProp) ? playerIdProp.GetString() : null;
        if (playerIdStr == null || !Guid.TryParse(playerIdStr, out var playerId)) return;

        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        if (_players.TryGetValue(playerId, out var player))
        {
            player.state.X = x;
            player.state.Y = y;
            player.state.Z = z;
            // SERVER AUTHORITY: Block client moves for 3 seconds after teleport
            player.state.ServerAuthorityUntil = DateTime.UtcNow.AddSeconds(3);
            Console.WriteLine($"[GameSession] ADMIN TELEPORT player={playerId} to ({x},{y},{z}) - SERVER AUTHORITY until {player.state.ServerAuthorityUntil.Value:HH:mm:ss.fff}");
            await BroadcastState();
        }
    }

    private async Task HandleAdminKick(JsonDocument doc)
    {
        var playerIdStr = doc.RootElement.TryGetProperty("playerId", out var playerIdProp) ? playerIdProp.GetString() : null;
        if (playerIdStr == null || !Guid.TryParse(playerIdStr, out var playerId)) return;

        if (_players.TryRemove(playerId, out var player))
        {
            if (player.socket != null && player.socket.State == WebSocketState.Open)
            {
                try { await player.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "kicked", CancellationToken.None); } catch { }
            }
            Console.WriteLine($"[GameSession] ADMIN KICK player={playerId}");
            await BroadcastState();
        }
    }

    private async Task HandleAdminTeleportAll(JsonDocument doc)
    {
        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        foreach (var (state, socket) in _players.Values)
        {
            if (!state.IsAdmin) // Don't teleport admins
            {
                state.X = x;
                state.Y = y;
                state.Z = z;
                // SERVER AUTHORITY: Block client moves for 3 seconds after teleport
                state.ServerAuthorityUntil = DateTime.UtcNow.AddSeconds(3);
            }
        }
        Console.WriteLine($"[GameSession] ADMIN TELEPORT ALL to ({x},{y},{z}) - SERVER AUTHORITY for 3 seconds");
        await BroadcastState();
    }

    private async Task HandleAdminKickAll()
    {
        var toKick = _players.Values.Where(p => !p.state.IsAdmin).ToList();
        foreach (var (state, socket) in toKick)
        {
            _players.TryRemove(state.Id, out _);
            if (socket != null && socket.State == WebSocketState.Open)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "kicked", CancellationToken.None); } catch { }
            }
        }
        Console.WriteLine($"[GameSession] ADMIN KICK ALL ({toKick.Count} players)");
        await BroadcastState();
    }
}
