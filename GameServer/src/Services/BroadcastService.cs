using System.Text.Json;
using GameServer.Interfaces;
using Shared.Models;

namespace GameServer.Services
{
    public interface IBroadcastService
    {
        Task BroadcastState(GameServer.UdpGameServer server, IPlayerRepository playerRepository, List<BotDto> externalBots);
        Task BroadcastToPlayer(GameServer.UdpGameServer server, Guid playerId, object message);
        Task BroadcastToAll(GameServer.UdpGameServer server, object message);
    }

    public class BroadcastService : IBroadcastService
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public BroadcastService()
        {
            _jsonOptions = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            };
        }

        public async Task BroadcastState(GameServer.UdpGameServer server, IPlayerRepository playerRepository, List<BotDto> externalBots)
        {
            // Get data
            var players = playerRepository.GetAllPlayers();
            
            // Combine regular players with external bots from Bot Server
            var playerDtos = new List<PlayerDto>();
            
            // Add regular players
            playerDtos.AddRange(players.Select(p => new PlayerDto(
                p.Id, p.Name, p.X, p.Y, p.Z, p.IsNPC)));
            
            // Add external bots from Bot Server
            playerDtos.AddRange(externalBots.Select(bot => new PlayerDto(
                bot.Id, bot.Name, bot.X, bot.Y, bot.Z, true)));

            // Only log occasionally to reduce spam
            if (DateTime.UtcNow.Second % 10 == 0 && DateTime.UtcNow.Millisecond < 100)
            {
                Console.WriteLine($"[BroadcastService] Broadcasting: {players.Count} players, {externalBots.Count} bots");
            }

            var metrics = GetBenchmarkMetrics(playerRepository, externalBots);
            var snap = new StateSnapshot("state", playerDtos, metrics);
            await server.BroadcastToAll(snap);
        }

        public async Task BroadcastToPlayer(GameServer.UdpGameServer server, Guid playerId, object message)
        {
            var session = server._clients.Values.FirstOrDefault(s => s.PlayerId == playerId);
            if (session != null)
            {
                await server.SendToClient(session.EndPoint, message);
            }
        }

        public async Task BroadcastToAll(GameServer.UdpGameServer server, object message)
        {
            await server.BroadcastToAll(message);
        }

        private ServerBenchmarkMetrics GetBenchmarkMetrics(IPlayerRepository playerRepository, List<BotDto> externalBots)
        {
            var totalPlayers = playerRepository.GetPlayerCount();
            var realPlayers = playerRepository.GetRealPlayerCount();
            var totalBots = playerRepository.GetBotCount() + externalBots.Count;
            var activeBots = playerRepository.GetActiveBotCount() + externalBots.Count(b => b.IsActive);
            
            return new ServerBenchmarkMetrics
            {
                TotalEntities = totalPlayers + externalBots.Count,
                RealPlayers = realPlayers,
                TotalBots = totalBots,
                ActiveBots = activeBots,
                UpdatesPerSecond = 20, // Frecuencia fija: 20 Hz
                MemoryUsageMB = GC.GetTotalMemory(false) / 1024 / 1024,
                UptimeSeconds = Environment.TickCount / 1000.0
            };
        }
    }
}
