using System.Collections.Concurrent;
using System.Text.Json;

namespace Backend;

public class GameSession : IDisposable
{
    private readonly ConcurrentDictionary<Guid, PlayerState> _players = new();
    private readonly ConcurrentDictionary<Guid, PlayerSession> _playerSessions = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _broadcastLock = new object();
    private DateTime _lastBroadcast = DateTime.MinValue;
    private DateTime _lastBotUpdate = DateTime.MinValue;
    private const int BROADCAST_THROTTLE_MS = 50; // Max 20 broadcasts per second
    private const int BROADCAST_THROTTLE_HIGH_LOAD_MS = 200; // 5 broadcasts per second when many bots
    private const int BOT_UPDATE_MS = 100; // Bot AI updates every 100ms
    private readonly Random _random = new Random();
    private readonly Timer _gameLoopTimer;
    private bool _disposed = false;
    
    // Integration with new Bot Management System
    private readonly BotManagementSystem _botManager;

    public GameSession()
    {
        // Initialize Bot Management System
        _botManager = BotManagementSystem.Instance;
        
        // Start independent game loop for bot updates
        _gameLoopTimer = new Timer(GameLoopTick, null, TimeSpan.FromMilliseconds(BOT_UPDATE_MS), TimeSpan.FromMilliseconds(BOT_UPDATE_MS));
        Console.WriteLine("[GameSession] Game loop timer started (bot updates every 100ms)");
        Console.WriteLine("[GameSession] Bot Management System integrated");
    }

    // Add UDP message handler
    public async Task HandleUdpMessage(PlayerSession session, JsonDocument doc, UdpGameServer server)
    {
        if (!doc.RootElement.TryGetProperty("op", out var opProp)) return;
        var op = opProp.GetString();

        switch (op)
        {
            case "join":
                await HandleUdpJoin(session, doc, server);
                break;
            case "input":
                await HandleUdpInput(session, doc, server);
                break;
            case "heartbeat":
                session.LastHeartbeat = DateTime.UtcNow;
                break;
            // New Bot Management System commands
            case "admin_create_bot_container":
                await HandleAdminCreateBotContainer(doc, server);
                break;
            case "admin_remove_bot_container":
                await HandleAdminRemoveBotContainer(doc, server);
                break;
            case "admin_spawn_bots":
                await HandleAdminSpawnBots(doc, server);
                break;
            case "admin_remove_all_bots":
                await HandleAdminRemoveAllBots(doc, server);
                break;
            case "admin_pause_bots":
                await HandleAdminPauseBots(doc, server);
                break;
            case "admin_resume_bots":
                await HandleAdminResumeBots(doc, server);
                break;
            case "admin_bot_stats":
                await HandleAdminBotStats(doc, server);
                break;
            // Existing admin commands
            case "admin_spawn_npc":
                await HandleAdminSpawnNPC(doc, server);
                break;
            case "admin_teleport":
                await HandleAdminTeleport(doc, server);
                break;
            case "admin_kick":
                await HandleAdminKick(doc, server);
                break;
            case "admin_teleport_all":
                await HandleAdminTeleportAll(doc, server);
                break;
            case "admin_kick_all":
                await HandleAdminKickAll(server);
                break;
            case "admin_spawn_bot":
                await HandleAdminSpawnBot(doc, server);
                break;
            case "admin_control_bot":
                await HandleAdminControlBot(doc, server);
                break;
            case "admin_bot_behavior":
                await HandleAdminBotBehavior(doc, server);
                break;
            case "admin_spawn_benchmark_bots":
                await HandleAdminSpawnBenchmarkBots(doc, server);
                break;
        }
    }

    private async Task HandleUdpJoin(PlayerSession session, JsonDocument doc, UdpGameServer server)
    {
        var name = doc.RootElement.TryGetProperty("name", out var nameProp) ? 
                   nameProp.GetString() ?? "Player" : "Player";
        
        var player = new PlayerState { Name = name[..Math.Min(name.Length, 16)] };
        session.PlayerId = player.Id;
        
        _players[player.Id] = player;
        _playerSessions[player.Id] = session;
        
        Console.WriteLine($"[GameSession] UDP JOIN id={player.Id} name={player.Name} endpoint={session.EndPoint}");
        
        // Send join acknowledgment
        await server.SendToClient(session.EndPoint, new JoinAck("join_ack", player.Id));
    }

    private async Task HandleUdpInput(PlayerSession session, JsonDocument doc, UdpGameServer server)
    {
        if (!_players.TryGetValue(session.PlayerId, out var player)) return;

        // Extract sequence number for duplicate detection
        var sequence = doc.RootElement.TryGetProperty("seq", out var seqP) ? seqP.GetUInt32() : 0;
        
        // Skip if this is an old packet (simple duplicate detection)
        if (sequence <= session.LastSequenceReceived) return;
        session.LastSequenceReceived = sequence;

        // Extract input data
        var inputX = doc.RootElement.TryGetProperty("x", out var ixP) ? ixP.GetSingle() : 0f;
        var inputY = doc.RootElement.TryGetProperty("y", out var iyP) ? iyP.GetSingle() : 0f;
        var speed = doc.RootElement.TryGetProperty("speed", out var speedP) ? speedP.GetSingle() : 5f;

        // Server authority check
        if (player.ServerAuthorityUntil.HasValue && DateTime.UtcNow < player.ServerAuthorityUntil.Value)
        {
            Console.WriteLine($"[GameSession] UDP INPUT IGNORED (SERVER AUTHORITY) id={player.Id}");
            return;
        }

        // Apply server-side movement
        var deltaTime = 0.05f; // 20Hz server tick
        var inputMagnitude = Math.Sqrt(inputX * inputX + inputY * inputY);
        if (inputMagnitude > 1f)
        {
            inputX /= (float)inputMagnitude;
            inputY /= (float)inputMagnitude;
        }

        var deltaX = inputX * speed * deltaTime;
        var deltaZ = inputY * speed * deltaTime;

        player.X += deltaX;
        player.Z += deltaZ;
        player.LastUpdate = DateTime.UtcNow;

        Console.WriteLine($"[GameSession] UDP INPUT seq={sequence} id={player.Id} pos=({player.X:F2},{player.Y:F2},{player.Z:F2})");
    }

    public async Task BroadcastStateUdp(UdpGameServer server)
    {
        // Get total entity count first for dynamic throttling
        var managedBots = _botManager.GetAllActiveBots();
        var totalEntities = _players.Count + managedBots.Count;
        
        // Dynamic throttling based on entity count
        var throttleMs = totalEntities switch
        {
            > 150 => 200, // 5 FPS for 150+ entities
            > 100 => 100, // 10 FPS for 100+ entities  
            > 50 => 75,   // ~13 FPS for 50+ entities
            _ => BROADCAST_THROTTLE_MS // 20 FPS for <50 entities
        };
        
        lock (_broadcastLock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastBroadcast).TotalMilliseconds < throttleMs)
            {
                return;
            }
            _lastBroadcast = now;
        }

        UpdateBots();

        // Combine regular players with managed bots
        var playerDtos = new List<PlayerDto>();
        
        // Add regular players
        playerDtos.AddRange(_players.Values.Select(p => new PlayerDto(
            p.Id, p.Name, p.X, p.Y, p.Z, p.IsNPC)));
        
        // Add managed bots from Bot Management System
        // var managedBots = _botManager.GetAllActiveBots(); // Already retrieved above
        // Only log occasionally to reduce spam
        if (DateTime.UtcNow.Second % 10 == 0 && DateTime.UtcNow.Millisecond < 100)
        {
            Console.WriteLine($"[DEBUG] Broadcasting state: {_players.Count} players, {managedBots.Count} managed bots");
        }
        playerDtos.AddRange(managedBots.Select(bot => new PlayerDto(
            bot.Id, bot.Name, bot.X, bot.Y, bot.Z, true)));

        var snap = new StateSnapshot("state", playerDtos, GetBenchmarkMetrics());
        await server.BroadcastToAll(snap);
    }

    private void GameLoopTick(object? state)
    {
        if (_disposed) return;
        
        try 
        {
            var botsToUpdate = _players.Values.Where(p => p.IsBot && p.BotBehavior != BotBehavior.Idle).ToList();
            if (botsToUpdate.Count > 0)
            {
                // Performance optimization: limit concurrent bot updates for large counts
                const int MAX_BOTS_PER_UPDATE = 200;
                var botsThisUpdate = botsToUpdate.Count > MAX_BOTS_PER_UPDATE ? 
                    botsToUpdate.Take(MAX_BOTS_PER_UPDATE).ToList() : 
                    botsToUpdate;
                
                bool anyBotMoved = false;
                int updatedCount = 0;
                
                foreach (var botState in botsThisUpdate)
                {
                    var oldX = botState.X;
                    var oldZ = botState.Z;
                    
                    try
                    {
                        UpdateBotBehavior(botState);
                        updatedCount++;
                        
                        // Check if bot actually moved (reduced logging for performance)
                        if (Math.Abs(oldX - botState.X) > 0.001f || Math.Abs(oldZ - botState.Z) > 0.001f)
                        {
                            anyBotMoved = true;
                            // Only log movement for debugging with smaller bot counts
                            if (botsToUpdate.Count <= 50)
                            {
                                Console.WriteLine($"[GameSession] BOT MOVED id={botState.Id} from=({oldX:F2},{oldZ:F2}) to=({botState.X:F2},{botState.Z:F2}) behavior={botState.BotBehavior}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GameSession] Bot update error for {botState.Id}: {ex.Message}");
                        // Set bot to idle to prevent repeated errors
                        botState.BotBehavior = BotBehavior.Idle;
                    }
                }
                
                // Broadcast state if any bot moved
                if (anyBotMoved)
                {
                    // Throttle broadcasts more aggressively with many bots
                    var broadcastThrottle = botsToUpdate.Count > 100 ? 200 : BROADCAST_THROTTLE_MS;
                    
                    if ((DateTime.UtcNow - _lastBroadcast).TotalMilliseconds >= broadcastThrottle)
                    {
                        _ = Task.Run(async () => await BroadcastStateUdp(null!)); // Will be set properly in UDP context
                    }
                }
                
                // Log performance info for large bot counts
                if (botsToUpdate.Count > 100)
                {
                    Console.WriteLine($"[GameSession] Bot Update: {updatedCount}/{botsToUpdate.Count} bots processed (limited for performance)");
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
        _botManager?.Dispose();
        Console.WriteLine("[GameSession] Game loop timer and bot management disposed");
    }

    // ============================================
    // NEW BOT MANAGEMENT SYSTEM ADMIN COMMANDS
    // ============================================

    public async Task HandleAdminCreateBotContainer(JsonDocument doc, UdpGameServer server)
    {
        var name = doc.RootElement.TryGetProperty("name", out var nameProp) ? 
                   nameProp.GetString() ?? "BotContainer" : "BotContainer";
        var maxBots = doc.RootElement.TryGetProperty("maxBots", out var maxProp) ? 
                      maxProp.GetInt32() : 50;

        try
        {
            var container = _botManager.CreateContainer(name, maxBots);
            Console.WriteLine($"[GameSession] ADMIN CREATE BOT CONTAINER name={name} id={container.Id} maxBots={maxBots}");
            
            // Send confirmation back to admin
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = $"Bot container '{name}' created with ID {container.Id}",
                containerId = container.Id
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameSession] ADMIN CREATE BOT CONTAINER ERROR: {ex.Message}");
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = false,
                message = $"Failed to create bot container: {ex.Message}"
            });
        }
    }

    public async Task HandleAdminRemoveBotContainer(JsonDocument doc, UdpGameServer server)
    {
        var containerIdStr = doc.RootElement.TryGetProperty("containerId", out var containerIdProp) ? 
                            containerIdProp.GetString() : null;
        
        if (containerIdStr == null || !Guid.TryParse(containerIdStr, out var containerId))
        {
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = false,
                message = "Invalid container ID"
            });
            return;
        }

        var success = _botManager.RemoveContainer(containerId);
        Console.WriteLine($"[GameSession] ADMIN REMOVE BOT CONTAINER id={containerId} success={success}");
        
        await server.SendToClient(server._clients.Keys.First(), new
        {
            op = "admin_response",
            success = success,
            message = success ? $"Container {containerId} removed" : "Container not found"
        });
        
        if (success)
        {
            await BroadcastStateUdp(server);
        }
    }

    public async Task HandleAdminSpawnBots(JsonDocument doc, UdpGameServer server)
    {
        var containerIdStr = doc.RootElement.TryGetProperty("containerId", out var containerIdProp) ? 
                            containerIdProp.GetString() : null;
        var count = doc.RootElement.TryGetProperty("count", out var countProp) ? 
                   countProp.GetInt32() : 10;
        var namePrefix = doc.RootElement.TryGetProperty("namePrefix", out var nameProp) ? 
                        nameProp.GetString() ?? "Bot" : "Bot";

        if (containerIdStr == null || !Guid.TryParse(containerIdStr, out var containerId))
        {
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = false,
                message = "Invalid container ID"
            });
            return;
        }

        var created = _botManager.CreateBotsInContainer(containerId, count, namePrefix);
        Console.WriteLine($"[GameSession] ADMIN SPAWN BOTS container={containerId} requested={count} created={created}");
        
        await server.SendToClient(server._clients.Keys.First(), new
        {
            op = "admin_response",
            success = created > 0,
            message = $"Created {created} bots in container",
            botsCreated = created
        });
        
        if (created > 0)
        {
            await BroadcastStateUdp(server);
        }
    }

    public async Task HandleAdminRemoveAllBots(JsonDocument doc, UdpGameServer server)
    {
        var containerIdStr = doc.RootElement.TryGetProperty("containerId", out var containerIdProp) ? 
                            containerIdProp.GetString() : null;

        if (containerIdStr != null && Guid.TryParse(containerIdStr, out var containerId))
        {
            // Remove bots from specific container
            _botManager.RemoveAllBotsInContainer(containerId);
            Console.WriteLine($"[GameSession] ADMIN REMOVE ALL BOTS container={containerId}");
            
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = $"All bots removed from container {containerId}"
            });
        }
        else
        {
            // Remove all bots from all containers
            _botManager.RemoveAllBots();
            Console.WriteLine($"[GameSession] ADMIN REMOVE ALL BOTS (global)");
            
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = "All bots removed from all containers"
            });
        }
        
        await BroadcastStateUdp(server);
    }

    public async Task HandleAdminPauseBots(JsonDocument doc, UdpGameServer server)
    {
        var containerIdStr = doc.RootElement.TryGetProperty("containerId", out var containerIdProp) ? 
                            containerIdProp.GetString() : null;

        if (containerIdStr != null && Guid.TryParse(containerIdStr, out var containerId))
        {
            _botManager.PauseAllBotsInContainer(containerId);
            Console.WriteLine($"[GameSession] ADMIN PAUSE BOTS container={containerId}");
            
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = $"Bots paused in container {containerId}"
            });
        }
        else
        {
            _botManager.PauseAllBots();
            Console.WriteLine($"[GameSession] ADMIN PAUSE ALL BOTS (global)");
            
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = "All bots paused"
            });
        }
    }

    public async Task HandleAdminResumeBots(JsonDocument doc, UdpGameServer server)
    {
        var containerIdStr = doc.RootElement.TryGetProperty("containerId", out var containerIdProp) ? 
                            containerIdProp.GetString() : null;

        if (containerIdStr != null && Guid.TryParse(containerIdStr, out var containerId))
        {
            _botManager.ResumeAllBotsInContainer(containerId);
            Console.WriteLine($"[GameSession] ADMIN RESUME BOTS container={containerId}");
            
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = $"Bots resumed in container {containerId}"
            });
        }
        else
        {
            _botManager.ResumeAllBots();
            Console.WriteLine($"[GameSession] ADMIN RESUME ALL BOTS (global)");
            
            await server.SendToClient(server._clients.Keys.First(), new
            {
                op = "admin_response",
                success = true,
                message = "All bots resumed"
            });
        }
    }

    public async Task HandleAdminBotStats(JsonDocument doc, UdpGameServer server)
    {
        var stats = _botManager.GetSystemStats();
        Console.WriteLine($"[GameSession] ADMIN BOT STATS containers={stats.TotalContainers} bots={stats.TotalBots}");
        
        await server.SendToClient(server._clients.Keys.First(), new
        {
            op = "admin_bot_stats_response",
            success = true,
            stats = new
            {
                totalContainers = stats.TotalContainers,
                activeContainers = stats.ActiveContainers,
                totalBots = stats.TotalBots,
                activeBots = stats.ActiveBots,
                pausedBots = stats.PausedBots,
                containers = stats.ContainerStats.Select(c => new
                {
                    id = c.ContainerId,
                    name = c.ContainerName,
                    totalBots = c.TotalBots,
                    activeBots = c.ActiveBots,
                    pausedBots = c.PausedBots,
                    maxBots = c.MaxBots,
                    isActive = c.IsActive,
                    createdAt = c.CreatedAt
                })
            }
        });
    }

    private void UpdateBots()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBotUpdate).TotalMilliseconds < BOT_UPDATE_MS)
            return;

        _lastBotUpdate = now;

        foreach (var state in _players.Values)
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
        
        if (_players.TryGetValue(bot.FollowTargetId.Value, out var targetBot))
        {
            var targetPos = new Vector3(targetBot.X, targetBot.Y, targetBot.Z);
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

    // Admin commands now work through UDP (no WebSocket admin needed)
    public async Task HandleAdminSpawnNPC(JsonDocument doc, UdpGameServer server)
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
        
        _players[npc.Id] = npc;
        Console.WriteLine($"[GameSession] ADMIN SPAWN NPC id={npc.Id} name={npc.Name} pos=({x},{y},{z})");
        await BroadcastStateUdp(server);
    }

    public async Task HandleAdminTeleport(JsonDocument doc, UdpGameServer server)
    {
        var playerIdStr = doc.RootElement.TryGetProperty("playerId", out var playerIdProp) ? playerIdProp.GetString() : null;
        if (playerIdStr == null || !Guid.TryParse(playerIdStr, out var playerId)) return;

        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        if (_players.TryGetValue(playerId, out var player))
        {
            player.X = x;
            player.Y = y;
            player.Z = z;
            // SERVER AUTHORITY: Block client moves for 3 seconds after teleport
            player.ServerAuthorityUntil = DateTime.UtcNow.AddSeconds(3);
            Console.WriteLine($"[GameSession] ADMIN TELEPORT player={playerId} to ({x},{y},{z}) - SERVER AUTHORITY until {player.ServerAuthorityUntil.Value:HH:mm:ss.fff}");
            await BroadcastStateUdp(server);
        }
    }

    public async Task HandleAdminKick(JsonDocument doc, UdpGameServer server)
    {
        var playerIdStr = doc.RootElement.TryGetProperty("playerId", out var playerIdProp) ? playerIdProp.GetString() : null;
        if (playerIdStr == null || !Guid.TryParse(playerIdStr, out var playerId)) return;

        if (_players.TryRemove(playerId, out var player))
        {
            // Remove from session tracking
            _playerSessions.TryRemove(playerId, out _);
            Console.WriteLine($"[GameSession] ADMIN KICK player={playerId}");
            await BroadcastStateUdp(server);
        }
    }

    public async Task HandleAdminTeleportAll(JsonDocument doc, UdpGameServer server)
    {
        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        foreach (var state in _players.Values)
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
        await BroadcastStateUdp(server);
    }

    public async Task HandleAdminKickAll(UdpGameServer server)
    {
        var toKick = _players.Values.Where(p => !p.IsAdmin).ToList();
        foreach (var state in toKick)
        {
            _players.TryRemove(state.Id, out _);
            _playerSessions.TryRemove(state.Id, out _);
        }
        Console.WriteLine($"[GameSession] ADMIN KICK ALL ({toKick.Count} players)");
        await BroadcastStateUdp(server);
    }

    public async Task HandleAdminSpawnBot(JsonDocument doc, UdpGameServer server)
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
        
        _players[bot.Id] = bot;
        Console.WriteLine($"[GameSession] ADMIN SPAWN BOT id={bot.Id} name={bot.Name} pos=({x},{y},{z}) behavior={behavior}");
        await BroadcastStateUdp(server);
    }

    public async Task HandleAdminControlBot(JsonDocument doc, UdpGameServer server)
    {
        var botIdStr = doc.RootElement.TryGetProperty("botId", out var botIdProp) ? botIdProp.GetString() : null;
        if (botIdStr == null || !Guid.TryParse(botIdStr, out var botId)) return;

        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        if (_players.TryGetValue(botId, out var player) && player.IsBot)
        {
            player.X = x;
            player.Y = y;
            player.Z = z;
            player.BotBehavior = BotBehavior.Idle; // Stop current behavior
            Console.WriteLine($"[GameSession] ADMIN CONTROL BOT bot={botId} to ({x},{y},{z})");
            await BroadcastStateUdp(server);
        }
    }

    public async Task HandleAdminBotBehavior(JsonDocument doc, UdpGameServer server)
    {
        var botIdStr = doc.RootElement.TryGetProperty("botId", out var botIdProp) ? botIdProp.GetString() : null;
        if (botIdStr == null || !Guid.TryParse(botIdStr, out var botId)) return;

        var behavior = doc.RootElement.TryGetProperty("behavior", out var behaviorProp) ? behaviorProp.GetString() ?? "idle" : "idle";
        var targetIdStr = doc.RootElement.TryGetProperty("targetId", out var targetIdProp) ? targetIdProp.GetString() : null;

        if (_players.TryGetValue(botId, out var player) && player.IsBot)
        {
            player.BotBehavior = ParseBotBehavior(behavior);
            
            if (behavior == "follow" && targetIdStr != null && Guid.TryParse(targetIdStr, out var targetId))
            {
                player.FollowTargetId = targetId;
            }
            else if (behavior == "patrol")
            {
                // Set up simple patrol waypoints (square pattern)
                var centerX = player.X;
                var centerZ = player.Z;
                player.PatrolWaypoints = new List<Vector3>
                {
                    new Vector3(centerX - 3, 0, centerZ - 3),
                    new Vector3(centerX + 3, 0, centerZ - 3),
                    new Vector3(centerX + 3, 0, centerZ + 3),
                    new Vector3(centerX - 3, 0, centerZ + 3)
                };
                player.CurrentWaypointIndex = 0;
            }
            
            Console.WriteLine($"[GameSession] ADMIN BOT BEHAVIOR bot={botId} behavior={behavior} target={targetIdStr}");
            await BroadcastStateUdp(server);
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

    public async Task HandleAdminSpawnBenchmarkBots(JsonDocument doc, UdpGameServer server)
    {
        var count = doc.RootElement.TryGetProperty("count", out var countProp) ? countProp.GetInt32() : 100;
        var behaviorStr = doc.RootElement.TryGetProperty("behavior", out var behaviorProp) ? behaviorProp.GetString() ?? "random" : "random";
        var spreadRadius = doc.RootElement.TryGetProperty("spreadRadius", out var radiusProp) ? radiusProp.GetSingle() : 50f;
        
        var behavior = ParseBotBehavior(behaviorStr);
        
        Console.WriteLine($"[GameSession] ADMIN SPAWN BENCHMARK BOTS count={count} behavior={behavior} spread={spreadRadius}");
        await SpawnBenchmarkBots(count, behavior, spreadRadius, server);
    }

    // ============================================
    // BENCHMARK & STRESS TESTING FUNCTIONS
    // ============================================

    /// <summary>
    /// Spawn multiple bots for server performance testing
    /// </summary>
    public async Task SpawnBenchmarkBots(int count = 100, BotBehavior behavior = BotBehavior.Random, float spreadRadius = 50f, UdpGameServer? server = null)
    {
        // Safety limits to prevent crashes
        const int MAX_BOTS = 500;
        const int CRITICAL_ENTITY_COUNT = 800;
        
        if (count > MAX_BOTS)
        {
            Console.WriteLine($"[BENCHMARK] ERROR: Requested {count} bots exceeds safety limit of {MAX_BOTS}");
            count = MAX_BOTS;
        }
        
        var currentEntities = _players.Count;
        if (currentEntities + count > CRITICAL_ENTITY_COUNT)
        {
            var safeCount = Math.Max(0, CRITICAL_ENTITY_COUNT - currentEntities);
            Console.WriteLine($"[BENCHMARK] WARNING: Reducing bot count from {count} to {safeCount} to prevent server overload");
            count = safeCount;
        }
        
        if (count <= 0)
        {
            Console.WriteLine($"[BENCHMARK] ERROR: Cannot spawn bots - server already at capacity ({currentEntities} entities)");
            return;
        }
        
        var startTime = DateTime.UtcNow;
        Console.WriteLine($"[BENCHMARK] Starting bot spawn: {count} bots with {behavior} behavior (safety limited)");
        
        var spawnedIds = new List<Guid>();
        var batchSize = Math.Min(5, count / 10); // Smaller batches for large counts
        
        for (int i = 0; i < count; i += batchSize)
        {
            var batchEnd = Math.Min(i + batchSize, count);
            
            for (int j = i; j < batchEnd; j++)
            {
                var botId = Guid.NewGuid();
                
                // Random position within spread radius
                var angle = _random.NextDouble() * Math.PI * 2;
                var distance = _random.NextDouble() * spreadRadius;
                var x = (float)(Math.Cos(angle) * distance);
                var z = (float)(Math.Sin(angle) * distance);
                
                var botState = new PlayerState
                {
                    Id = botId,
                    Name = $"BenchBot_{j:D3}",
                    X = x,
                    Y = 0,
                    Z = z,
                    IsBot = true,
                    BotBehavior = behavior,
                    BotMoveDirection = new Vector3(0, 0, 0),
                    BotSpeed = _random.Next(2, 6), // Random speed 2-5
                    LastUpdate = DateTime.UtcNow
                };
                
                // Add random movement target for moving bots
                if (behavior == BotBehavior.Random)
                {
                    botState.BotMoveDirection = new Vector3(
                        (float)(_random.NextDouble() - 0.5) * 2,
                        0,
                        (float)(_random.NextDouble() - 0.5) * 2
                    );
                }
                else if (behavior == BotBehavior.Patrol)
                {
                    // Set up patrol waypoints
                    botState.PatrolWaypoints = new List<Vector3>
                    {
                        new Vector3(x - 5, 0, z - 5),
                        new Vector3(x + 5, 0, z - 5),
                        new Vector3(x + 5, 0, z + 5),
                        new Vector3(x - 5, 0, z + 5)
                    };
                    botState.CurrentWaypointIndex = 0;
                }
                
                _players.TryAdd(botId, botState);
                spawnedIds.Add(botId);
            }
            
            // Longer delay between batches for large counts to prevent overload
            if (i + batchSize < count)
            {
                var delay = count > 200 ? 100 : 50; // Slower spawning for large counts
                await Task.Delay(delay);
            }
        }
        
        var duration = DateTime.UtcNow - startTime;
        Console.WriteLine($"[BENCHMARK] Spawned {spawnedIds.Count} bots in {duration.TotalMilliseconds:F2}ms");
        Console.WriteLine($"[BENCHMARK] Total entities: {_players.Count} (Active bots: {_players.Values.Count(p => p.IsBot && p.BotBehavior != BotBehavior.Idle)})");
        
        // Only broadcast if entity count is reasonable and server is available
        if (_players.Count <= CRITICAL_ENTITY_COUNT && server != null)
        {
            await BroadcastStateUdp(server);
        }
        else
        {
            Console.WriteLine($"[BENCHMARK] WARNING: Skipping broadcast - too many entities ({_players.Count}) or no server");
        }
        
        // Log performance metrics
        LogBenchmarkMetrics(spawnedIds.Count);
    }

    /// <summary>
    /// Remove all benchmark bots
    /// </summary>
    public async Task ClearBenchmarkBots(UdpGameServer? server = null)
    {
        var startTime = DateTime.UtcNow;
        var botIds = _players.Where(p => p.Value.IsBot && p.Value.Name.StartsWith("BenchBot_"))
                            .Select(p => p.Key)
                            .ToList();
        
        Console.WriteLine($"[BENCHMARK] Removing {botIds.Count} benchmark bots...");
        
        foreach (var botId in botIds)
        {
            _players.TryRemove(botId, out _);
        }
        
        var duration = DateTime.UtcNow - startTime;
        Console.WriteLine($"[BENCHMARK] Removed {botIds.Count} bots in {duration.TotalMilliseconds:F2}ms");
        Console.WriteLine($"[BENCHMARK] Remaining entities: {_players.Count}");
        
        if (server != null)
        {
            await BroadcastStateUdp(server);
        }
    }

    /// <summary>
    /// Get current server performance metrics
    /// </summary>
    public ServerBenchmarkMetrics GetBenchmarkMetrics()
    {
        var totalPlayers = _players.Count;
        var realPlayers = _players.Count(p => !p.Value.IsBot);
        var totalBots = _players.Count(p => p.Value.IsBot);
        var activeBots = _players.Count(p => p.Value.IsBot && p.Value.BotBehavior != BotBehavior.Idle);
        
        return new ServerBenchmarkMetrics
        {
            TotalEntities = totalPlayers,
            RealPlayers = realPlayers,
            TotalBots = totalBots,
            ActiveBots = activeBots,
            UpdatesPerSecond = 1000 / BOT_UPDATE_MS, // Theoretical max
            MemoryUsageMB = GC.GetTotalMemory(false) / 1024 / 1024,
            UptimeSeconds = (DateTime.UtcNow - (_gameLoopStartTime ?? DateTime.UtcNow)).TotalSeconds
        };
    }

    private DateTime? _gameLoopStartTime = DateTime.UtcNow;

    private void LogBenchmarkMetrics(int newBots)
    {
        var metrics = GetBenchmarkMetrics();
        Console.WriteLine($"[BENCHMARK METRICS]");
        Console.WriteLine($"  Total Entities: {metrics.TotalEntities}");
        Console.WriteLine($"  Real Players: {metrics.RealPlayers}");
        Console.WriteLine($"  Total Bots: {metrics.TotalBots}");
        Console.WriteLine($"  Active Bots: {metrics.ActiveBots}");
        Console.WriteLine($"  Memory Usage: {metrics.MemoryUsageMB:F2} MB");
        Console.WriteLine($"  Bot Update Rate: {metrics.UpdatesPerSecond} Hz");
        Console.WriteLine($"  Uptime: {metrics.UptimeSeconds:F2} seconds");
    }
}

public class ServerBenchmarkMetrics
{
    public int TotalEntities { get; set; }
    public int RealPlayers { get; set; }
    public int TotalBots { get; set; }
    public int ActiveBots { get; set; }
    public double UpdatesPerSecond { get; set; }
    public long MemoryUsageMB { get; set; }
    public double UptimeSeconds { get; set; }
}
