using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Backend;

public class GameSession : IDisposable
{
    private readonly ConcurrentDictionary<Guid, (PlayerState state, WebSocket socket)> _players = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _broadcastLock = new object();
    private DateTime _lastBroadcast = DateTime.MinValue;
    private DateTime _lastBotUpdate = DateTime.MinValue;
    private const int BROADCAST_THROTTLE_MS = 50; // Max 20 broadcasts per second
    private const int BOT_UPDATE_MS = 100; // Bot AI updates every 100ms
    private readonly Random _random = new Random();
    private readonly Timer _gameLoopTimer;
    private bool _disposed = false;

    public GameSession()
    {
        // Start independent game loop for bot updates
        _gameLoopTimer = new Timer(GameLoopTick, null, TimeSpan.FromMilliseconds(BOT_UPDATE_MS), TimeSpan.FromMilliseconds(BOT_UPDATE_MS));
        Console.WriteLine("[GameSession] Game loop timer started (bot updates every 100ms)");
    }

    private void GameLoopTick(object? state)
    {
        if (_disposed) return;
        
        try 
        {
            var botsToUpdate = _players.Values.Where(p => p.state.IsBot && p.state.BotBehavior != BotBehavior.Idle).ToList();
            if (botsToUpdate.Count > 0)
            {
                bool anyBotMoved = false;
                foreach (var (botState, socket) in botsToUpdate)
                {
                    var oldX = botState.X;
                    var oldZ = botState.Z;
                    
                    UpdateBotBehavior(botState);
                    
                    // Check if bot actually moved
                    if (Math.Abs(oldX - botState.X) > 0.001f || Math.Abs(oldZ - botState.Z) > 0.001f)
                    {
                        anyBotMoved = true;
                        Console.WriteLine($"[GameSession] BOT MOVED id={botState.Id} from=({oldX:F2},{oldZ:F2}) to=({botState.X:F2},{botState.Z:F2}) behavior={botState.BotBehavior}");
                    }
                }
                
                // Broadcast state if any bot moved
                if (anyBotMoved)
                {
                    _ = Task.Run(async () => await BroadcastState());
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameSession] Game loop error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _gameLoopTimer?.Dispose();
        Console.WriteLine("[GameSession] Game loop timer disposed");
    }

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
                    case "input":
                        if (me == null || me.IsAdmin) break;
                        
                        // SERVER AUTHORITY: Ignore input during server authority period
                        if (me.ServerAuthorityUntil.HasValue && DateTime.UtcNow < me.ServerAuthorityUntil.Value)
                        {
                            Console.WriteLine($"[GameSession] INPUT IGNORED (SERVER AUTHORITY) id={me.Id} authority_until={me.ServerAuthorityUntil.Value:HH:mm:ss.fff}");
                            break;
                        }
                        
                        // Get input data (enhanced with sequence for client-side prediction)
                        var sequence = doc.RootElement.TryGetProperty("seq", out var seqP) ? seqP.GetUInt32() : 0;
                        var inputX = doc.RootElement.TryGetProperty("x", out var ixP) ? ixP.GetSingle() : 0f;
                        var inputY = doc.RootElement.TryGetProperty("y", out var iyP) ? iyP.GetSingle() : 0f;
                        var inputZ = doc.RootElement.TryGetProperty("z", out var izP) ? izP.GetSingle() : 0f; // Legacy support
                        var speed = doc.RootElement.TryGetProperty("speed", out var speedP) ? speedP.GetSingle() : 5f;
                        var clientTime = doc.RootElement.TryGetProperty("time", out var timeP) ? timeP.GetSingle() : 0f;
                        
                        // Server-side movement calculation (tick-based)
                        var deltaTime = 0.05f; // 20Hz server tick rate
                        var inputMagnitude = Math.Sqrt(inputX * inputX + inputY * inputY + inputZ * inputZ);
                        if (inputMagnitude > 1f) // Normalize if magnitude > 1
                        {
                            inputX /= (float)inputMagnitude;
                            inputY /= (float)inputMagnitude;
                            inputZ /= (float)inputMagnitude;
                        }
                        
                        // Apply movement with server physics
                        var deltaX = inputX * speed * deltaTime;
                        var deltaY = inputY * speed * deltaTime;
                        var deltaZ = (inputZ != 0 ? inputZ : inputY) * speed * deltaTime; // Support both 2D and 3D input
                        
                        me.X += deltaX;
                        me.Y += 0; // Keep Y at 0 for ground-based movement
                        me.Z += deltaZ;
                        me.LastUpdate = DateTime.UtcNow;
                        
                        Console.WriteLine($"[GameSession] INPUT seq={sequence} id={me.Id} input=({inputX:F2},{inputY:F2}) speed={speed:F1} pos=({me.X:F2},{me.Y:F2},{me.Z:F2})");
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
                        
                        // DEPRECATED: Still accept old move messages for compatibility, but log warning
                        if (doc.RootElement.TryGetProperty("x", out var xP)) me.X = xP.GetSingle();
                        if (doc.RootElement.TryGetProperty("y", out var yP)) me.Y = yP.GetSingle();
                        if (doc.RootElement.TryGetProperty("z", out var zP)) me.Z = zP.GetSingle();
                        me.LastUpdate = DateTime.UtcNow;
                        Console.WriteLine($"[GameSession] MOVE ACCEPTED (DEPRECATED) id={me.Id} pos=({me.X},{me.Y},{me.Z}) - Consider using 'input' messages");
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
                    case "admin_spawn_bot":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminSpawnBot(doc);
                        break;
                    case "admin_control_bot":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminControlBot(doc);
                        break;
                    case "admin_bot_behavior":
                        if (me == null || !me.IsAdmin) break;
                        await HandleAdminBotBehavior(doc);
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
        
        // Update bots before broadcasting
        UpdateBots();
        
        return BroadcastState();
    }

    private void UpdateBots()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBotUpdate).TotalMilliseconds < BOT_UPDATE_MS)
            return;

        _lastBotUpdate = now;

        foreach (var (state, socket) in _players.Values)
        {
            if (!state.IsBot || state.BotBehavior == BotBehavior.Idle)
                continue;

            UpdateBotBehavior(state);
        }
    }

    private void UpdateBotBehavior(PlayerState bot)
    {
        var deltaTime = 0.1f; // 100ms update rate
        
        switch (bot.BotBehavior)
        {
            case BotBehavior.Random:
                UpdateBotRandom(bot, deltaTime);
                break;
            case BotBehavior.Follow:
                UpdateBotFollow(bot, deltaTime);
                break;
            case BotBehavior.Patrol:
                UpdateBotPatrol(bot, deltaTime);
                break;
        }
        
        bot.LastUpdate = DateTime.UtcNow;
    }

    private void UpdateBotRandom(PlayerState bot, float deltaTime)
    {
        // Change direction every 2 seconds, but move continuously
        if ((DateTime.UtcNow - bot.LastBotUpdate).TotalSeconds > 2)
        {
            // Generate new random direction
            bot.BotMoveDirection = new Vector3(
                (_random.NextSingle() - 0.5f) * 2f,
                0,
                (_random.NextSingle() - 0.5f) * 2f
            ).Normalized();
            bot.LastBotUpdate = DateTime.UtcNow;
            Console.WriteLine($"[GameSession] BOT RANDOM new direction id={bot.Id} dir=({bot.BotMoveDirection.Value.X:F2},{bot.BotMoveDirection.Value.Z:F2})");
        }
        
        // Apply continuous movement
        if (bot.BotMoveDirection.HasValue)
        {
            bot.X += bot.BotMoveDirection.Value.X * bot.BotSpeed * deltaTime;
            bot.Z += bot.BotMoveDirection.Value.Z * bot.BotSpeed * deltaTime;
        }
    }

    private void UpdateBotFollow(PlayerState bot, float deltaTime)
    {
        if (bot.FollowTargetId == null) return;
        
        if (_players.TryGetValue(bot.FollowTargetId.Value, out var target))
        {
            var targetPos = new Vector3(target.state.X, target.state.Y, target.state.Z);
            var botPos = new Vector3(bot.X, bot.Y, bot.Z);
            
            var distance = botPos.Distance(targetPos);
            if (distance > 2f) // Follow if more than 2 units away
            {
                var direction = new Vector3(
                    targetPos.X - botPos.X,
                    0,
                    targetPos.Z - botPos.Z
                ).Normalized();
                
                bot.X += direction.X * bot.BotSpeed * deltaTime;
                bot.Z += direction.Z * bot.BotSpeed * deltaTime;
            }
        }
    }

    private void UpdateBotPatrol(PlayerState bot, float deltaTime)
    {
        if (bot.PatrolWaypoints.Count == 0) return;
        
        var currentTarget = bot.PatrolWaypoints[bot.CurrentWaypointIndex];
        var botPos = new Vector3(bot.X, bot.Y, bot.Z);
        
        var distance = botPos.Distance(currentTarget);
        if (distance < 0.5f) // Reached waypoint
        {
            bot.CurrentWaypointIndex = (bot.CurrentWaypointIndex + 1) % bot.PatrolWaypoints.Count;
            currentTarget = bot.PatrolWaypoints[bot.CurrentWaypointIndex];
        }
        
        var direction = new Vector3(
            currentTarget.X - botPos.X,
            0,
            currentTarget.Z - botPos.Z
        ).Normalized();
        
        bot.X += direction.X * bot.BotSpeed * deltaTime;
        bot.Z += direction.Z * bot.BotSpeed * deltaTime;
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

    private async Task HandleAdminSpawnBot(JsonDocument doc)
    {
        var name = doc.RootElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Bot" : "Bot";
        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;
        var behavior = doc.RootElement.TryGetProperty("behavior", out var behaviorProp) ? behaviorProp.GetString() ?? "idle" : "idle";

        var bot = new PlayerState 
        { 
            Name = name[..Math.Min(name.Length, 16)], 
            X = x, Y = y, Z = z, 
            IsNPC = true,
            IsBot = true,
            BotSpeed = 3f,
            BotBehavior = ParseBotBehavior(behavior)
        };
        
        // Bots don't have real WebSocket, use null
        _players[bot.Id] = (bot, null!);
        Console.WriteLine($"[GameSession] ADMIN SPAWN BOT id={bot.Id} name={bot.Name} pos=({x},{y},{z}) behavior={behavior}");
        await BroadcastState();
    }

    private async Task HandleAdminControlBot(JsonDocument doc)
    {
        var botIdStr = doc.RootElement.TryGetProperty("botId", out var botIdProp) ? botIdProp.GetString() : null;
        if (botIdStr == null || !Guid.TryParse(botIdStr, out var botId)) return;

        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        if (_players.TryGetValue(botId, out var player) && player.state.IsBot)
        {
            player.state.X = x;
            player.state.Y = y;
            player.state.Z = z;
            player.state.BotBehavior = BotBehavior.Idle; // Stop current behavior
            Console.WriteLine($"[GameSession] ADMIN CONTROL BOT bot={botId} to ({x},{y},{z})");
            await BroadcastState();
        }
    }

    private async Task HandleAdminBotBehavior(JsonDocument doc)
    {
        var botIdStr = doc.RootElement.TryGetProperty("botId", out var botIdProp) ? botIdProp.GetString() : null;
        if (botIdStr == null || !Guid.TryParse(botIdStr, out var botId)) return;

        var behavior = doc.RootElement.TryGetProperty("behavior", out var behaviorProp) ? behaviorProp.GetString() ?? "idle" : "idle";
        var targetIdStr = doc.RootElement.TryGetProperty("targetId", out var targetIdProp) ? targetIdProp.GetString() : null;

        if (_players.TryGetValue(botId, out var player) && player.state.IsBot)
        {
            player.state.BotBehavior = ParseBotBehavior(behavior);
            
            if (behavior == "follow" && targetIdStr != null && Guid.TryParse(targetIdStr, out var targetId))
            {
                player.state.FollowTargetId = targetId;
            }
            else if (behavior == "patrol")
            {
                // Set up simple patrol waypoints (square pattern)
                var centerX = player.state.X;
                var centerZ = player.state.Z;
                player.state.PatrolWaypoints = new List<Vector3>
                {
                    new Vector3(centerX - 3, 0, centerZ - 3),
                    new Vector3(centerX + 3, 0, centerZ - 3),
                    new Vector3(centerX + 3, 0, centerZ + 3),
                    new Vector3(centerX - 3, 0, centerZ + 3)
                };
                player.state.CurrentWaypointIndex = 0;
            }
            
            Console.WriteLine($"[GameSession] ADMIN BOT BEHAVIOR bot={botId} behavior={behavior} target={targetIdStr}");
            await BroadcastState();
        }
    }

    private BotBehavior ParseBotBehavior(string behavior)
    {
        return behavior.ToLower() switch
        {
            "random" => BotBehavior.Random,
            "follow" => BotBehavior.Follow,
            "patrol" => BotBehavior.Patrol,
            "moveto" => BotBehavior.MoveTo,
            _ => BotBehavior.Idle
        };
    }
}
