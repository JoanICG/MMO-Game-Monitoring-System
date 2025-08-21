using System.Text.Json;
using Backend.Interfaces;

namespace Backend.Services
{
    public interface IBroadcastService
    {
        Task BroadcastState(UdpGameServer server, IPlayerRepository playerRepository, BotManagementSystem botManager);
        Task BroadcastToPlayer(UdpGameServer server, Guid playerId, object message);
        Task BroadcastToAll(UdpGameServer server, object message);
        bool ShouldThrottleBroadcast(int entityCount);
        
        // Legacy compatibility
        Task BroadcastStateAsync(IEnumerable<PlayerState> players, IEnumerable<Bot> bots, object server);
        bool ShouldBroadcast();
    }

    public class BroadcastService : IBroadcastService
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly object _broadcastLock = new();
        private DateTime _lastBroadcast = DateTime.MinValue;
        private const int BROADCAST_THROTTLE_MS = 50; // Max 20 broadcasts per second

        public BroadcastService()
        {
            _jsonOptions = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            };
        }

        public async Task BroadcastState(UdpGameServer server, IPlayerRepository playerRepository, BotManagementSystem botManager)
        {
            // Get total entity count first for dynamic throttling
            var managedBots = botManager.GetAllActiveBots();
            var players = playerRepository.GetAllPlayers();
            var totalEntities = players.Count + managedBots.Count;
            
            // Dynamic throttling based on entity count
            if (ShouldThrottleBroadcast(totalEntities))
            {
                return;
            }

            // Combine regular players with managed bots
            var playerDtos = new List<PlayerDto>();
            
            // Add regular players
            playerDtos.AddRange(players.Select(p => new PlayerDto(
                p.Id, p.Name, p.X, p.Y, p.Z, p.IsNPC)));
            
            // Add managed bots from Bot Management System
            // Only log occasionally to reduce spam
            if (DateTime.UtcNow.Second % 10 == 0 && DateTime.UtcNow.Millisecond < 100)
            {
                Console.WriteLine($"[BroadcastService] Broadcasting state: {players.Count} players, {managedBots.Count} managed bots");
            }
            playerDtos.AddRange(managedBots.Select(bot => new PlayerDto(
                bot.Id, bot.Name, bot.X, bot.Y, bot.Z, true)));

            var metrics = GetBenchmarkMetrics(playerRepository, managedBots);
            var snap = new StateSnapshot("state", playerDtos, metrics);
            await server.BroadcastToAll(snap);
        }

        public async Task BroadcastToPlayer(UdpGameServer server, Guid playerId, object message)
        {
            // Find player session and send message
            var session = server._clients.Values.FirstOrDefault(s => s.PlayerId == playerId);
            if (session != null)
            {
                await server.SendToClient(session.EndPoint, message);
            }
        }

        public async Task BroadcastToAll(UdpGameServer server, object message)
        {
            await server.BroadcastToAll(message);
        }

        public bool ShouldThrottleBroadcast(int entityCount)
        {
            var throttleMs = entityCount switch
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
                    return true; // Should throttle
                }
                _lastBroadcast = now;
                return false; // Should not throttle
            }
        }

        private ServerBenchmarkMetrics GetBenchmarkMetrics(IPlayerRepository playerRepository, IEnumerable<Bot> managedBots)
        {
            var totalPlayers = playerRepository.GetPlayerCount();
            var realPlayers = playerRepository.GetRealPlayerCount();
            var totalBots = playerRepository.GetBotCount() + managedBots.Count();
            var activeBots = playerRepository.GetActiveBotCount() + managedBots.Count(b => b.IsActive);
            
            return new ServerBenchmarkMetrics
            {
                TotalEntities = totalPlayers + managedBots.Count(),
                RealPlayers = realPlayers,
                TotalBots = totalBots,
                ActiveBots = activeBots,
                UpdatesPerSecond = 10, // 10 Hz game loop
                MemoryUsageMB = GC.GetTotalMemory(false) / 1024 / 1024,
                UptimeSeconds = (DateTime.UtcNow - DateTime.UtcNow).TotalSeconds // Would need proper startup time
            };
        }

        // Legacy compatibility methods
        public bool ShouldBroadcast()
        {
            lock (_broadcastLock)
            {
                var now = DateTime.UtcNow;
                var throttleMs = CalculateDynamicThrottle();
                
                if ((now - _lastBroadcast).TotalMilliseconds < throttleMs)
                {
                    return false;
                }
                _lastBroadcast = now;
                return true;
            }
        }

        public async Task BroadcastStateAsync(IEnumerable<PlayerState> players, IEnumerable<Bot> bots, object server)
        {
            if (!ShouldBroadcast()) return;

            var playerDtos = new List<PlayerDto>();
            
            // Add regular players
            playerDtos.AddRange(players.Select(p => new PlayerDto(
                p.Id, p.Name, p.X, p.Y, p.Z, p.IsNPC)));
            
            // Add managed bots
            var botList = bots.ToList();
            LogBroadcastInfo(players.Count(), botList.Count);
            
            playerDtos.AddRange(botList.Select(bot => new PlayerDto(
                bot.Id, bot.Name, bot.X, bot.Y, bot.Z, true)));

            var snapshot = new StateSnapshot("state", playerDtos, CreateBenchmarkMetrics(players.Count()));
            
            // Use reflection to call BroadcastToAll - not ideal but keeps abstraction
            var broadcastMethod = server.GetType().GetMethod("BroadcastToAll");
            if (broadcastMethod != null)
            {
                await (Task)broadcastMethod.Invoke(server, new[] { snapshot })!;
            }
        }

        private int CalculateDynamicThrottle()
        {
            // This could be enhanced to consider actual entity count
            return BROADCAST_THROTTLE_MS;
        }

        private void LogBroadcastInfo(int playerCount, int botCount)
        {
            // Only log occasionally to reduce spam
            if (DateTime.UtcNow.Second % 10 == 0 && DateTime.UtcNow.Millisecond < 100)
            {
                Console.WriteLine($"[BroadcastService] Broadcasting state: {playerCount} players, {botCount} managed bots");
            }
        }

        private ServerBenchmarkMetrics CreateBenchmarkMetrics(int playerCount)
        {
            return new ServerBenchmarkMetrics
            {
                TotalEntities = playerCount,
                RealPlayers = playerCount,
                TotalBots = 0,
                ActiveBots = 0,
                UpdatesPerSecond = 20,
                MemoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024),
                UptimeSeconds = Environment.TickCount / 1000.0
            };
        }
    }
}
