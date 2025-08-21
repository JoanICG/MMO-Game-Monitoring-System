using System.Collections.Concurrent;
using System.Text.Json;
using Backend.Interfaces;
using Backend.Services;

namespace Backend;

public class GameSession : IDisposable
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IBroadcastService _broadcastService;
    private readonly IInputHandler _inputHandler;
    private readonly IGameLoopService _gameLoopService;
    private readonly BotManagementSystem _botManager;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private bool _disposed = false;
    private DateTime? _gameLoopStartTime = DateTime.UtcNow;

    public GameSession(
        IPlayerRepository? playerRepository = null,
        IBroadcastService? broadcastService = null,
        IInputHandler? inputHandler = null,
        IGameLoopService? gameLoopService = null)
    {
        // Use dependency injection or create default implementations
        _playerRepository = playerRepository ?? new PlayerRepository();
        _broadcastService = broadcastService ?? new BroadcastService();
        _inputHandler = inputHandler ?? new InputHandler();
        _gameLoopService = gameLoopService ?? new GameLoopService(_playerRepository, _broadcastService);

        // Initialize Bot Management System
        _botManager = BotManagementSystem.Instance;
        
        // Start game loop
        _gameLoopService.Start();
        
        Console.WriteLine("[GameSession] Initialized with dependency injection pattern");
        Console.WriteLine("[GameSession] Bot Management System integrated");
    }

    // Add UDP message handler - now delegates to services
    public async Task HandleUdpMessage(PlayerSession session, JsonDocument doc, UdpGameServer server)
    {
        if (!doc.RootElement.TryGetProperty("op", out var opProp)) return;
        var op = opProp.GetString();

        switch (op)
        {
            case "join":
                await _inputHandler.HandleJoin(session, doc, server, _playerRepository);
                break;
            case "input":
                await _inputHandler.HandleInput(session, doc, server, _playerRepository);
                break;
            case "heartbeat":
                session.LastHeartbeat = DateTime.UtcNow;
                break;
            // Bot Management System admin commands
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
            // Legacy admin commands
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

    // Delegate broadcast to service
    public async Task BroadcastStateUdp(UdpGameServer server)
    {
        await _broadcastService.BroadcastState(server, _playerRepository, _botManager);
    }

    // Dispose method - now delegates to services
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _gameLoopService?.Dispose();
        _botManager?.Dispose();
        
        Console.WriteLine("[GameSession] Services disposed");
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

    // Legacy admin commands - simplified by using repository
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
        
        _playerRepository.AddPlayer(npc);
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

        var player = _playerRepository.GetPlayer(playerId);
        if (player != null)
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

        if (_playerRepository.RemovePlayer(playerId))
        {
            Console.WriteLine($"[GameSession] ADMIN KICK player={playerId}");
            await BroadcastStateUdp(server);
        }
    }

    public async Task HandleAdminTeleportAll(JsonDocument doc, UdpGameServer server)
    {
        var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
        var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
        var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;

        var players = _playerRepository.GetAllPlayers();
        foreach (var state in players)
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
        var kickedCount = _playerRepository.RemoveAllNonAdminPlayers();
        Console.WriteLine($"[GameSession] ADMIN KICK ALL ({kickedCount} players)");
        await BroadcastStateUdp(server);
    }

    // Legacy bot commands - simplified using repository
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
        
        _playerRepository.AddPlayer(bot);
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

        var player = _playerRepository.GetPlayer(botId);
        if (player != null && player.IsBot)
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

        var player = _playerRepository.GetPlayer(botId);
        if (player != null && player.IsBot)
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

    // Benchmark methods - simplified using repository
    public async Task HandleAdminSpawnBenchmarkBots(JsonDocument doc, UdpGameServer server)
    {
        var count = doc.RootElement.TryGetProperty("count", out var countProp) ? countProp.GetInt32() : 100;
        var behaviorStr = doc.RootElement.TryGetProperty("behavior", out var behaviorProp) ? behaviorProp.GetString() ?? "random" : "random";
        var spreadRadius = doc.RootElement.TryGetProperty("spreadRadius", out var radiusProp) ? radiusProp.GetSingle() : 50f;
        
        var behavior = ParseBotBehavior(behaviorStr);
        
        Console.WriteLine($"[GameSession] ADMIN SPAWN BENCHMARK BOTS count={count} behavior={behavior} spread={spreadRadius}");
        await SpawnBenchmarkBots(count, behavior, spreadRadius, server);
    }

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
        
        var currentEntities = _playerRepository.GetPlayerCount();
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
        var random = new Random();
        
        for (int i = 0; i < count; i += batchSize)
        {
            var batchEnd = Math.Min(i + batchSize, count);
            
            for (int j = i; j < batchEnd; j++)
            {
                var botId = Guid.NewGuid();
                
                // Random position within spread radius
                var angle = random.NextDouble() * Math.PI * 2;
                var distance = random.NextDouble() * spreadRadius;
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
                    BotSpeed = random.Next(2, 6), // Random speed 2-5
                    LastUpdate = DateTime.UtcNow
                };
                
                // Add random movement target for moving bots
                if (behavior == BotBehavior.Random)
                {
                    botState.BotMoveDirection = new Vector3(
                        (float)(random.NextDouble() - 0.5) * 2,
                        0,
                        (float)(random.NextDouble() - 0.5) * 2
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
                
                _playerRepository.AddPlayer(botState);
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
        Console.WriteLine($"[BENCHMARK] Total entities: {_playerRepository.GetPlayerCount()} (Active bots: {_playerRepository.GetActiveBotCount()})");
        
        // Only broadcast if entity count is reasonable and server is available
        if (_playerRepository.GetPlayerCount() <= CRITICAL_ENTITY_COUNT && server != null)
        {
            await BroadcastStateUdp(server);
        }
        else
        {
            Console.WriteLine($"[BENCHMARK] WARNING: Skipping broadcast - too many entities ({_playerRepository.GetPlayerCount()}) or no server");
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
        var botCount = _playerRepository.RemoveBenchmarkBots();
        
        var duration = DateTime.UtcNow - startTime;
        Console.WriteLine($"[BENCHMARK] Removed {botCount} bots in {duration.TotalMilliseconds:F2}ms");
        Console.WriteLine($"[BENCHMARK] Remaining entities: {_playerRepository.GetPlayerCount()}");
        
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
        var totalPlayers = _playerRepository.GetPlayerCount();
        var realPlayers = _playerRepository.GetRealPlayerCount();
        var totalBots = _playerRepository.GetBotCount();
        var activeBots = _playerRepository.GetActiveBotCount();
        
        return new ServerBenchmarkMetrics
        {
            TotalEntities = totalPlayers,
            RealPlayers = realPlayers,
            TotalBots = totalBots,
            ActiveBots = activeBots,
            UpdatesPerSecond = _gameLoopService.IsRunning ? 10 : 0, // 10 Hz game loop
            MemoryUsageMB = GC.GetTotalMemory(false) / 1024 / 1024,
            UptimeSeconds = (DateTime.UtcNow - (_gameLoopStartTime ?? DateTime.UtcNow)).TotalSeconds
        };
    }

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
