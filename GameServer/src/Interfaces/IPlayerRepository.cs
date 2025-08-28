using System.Collections.Concurrent;

namespace GameServer.Interfaces
{
    public interface IPlayerRepository
    {
        void AddPlayer(Shared.Models.PlayerState player);
        bool RemovePlayer(Guid playerId);
        Shared.Models.PlayerState? GetPlayer(Guid playerId);
        IReadOnlyCollection<Shared.Models.PlayerState> GetAllPlayers();
        int GetPlayerCount();
        int GetRealPlayerCount();
        int GetBotCount();
        int GetActiveBotCount();
        int RemoveAllNonAdminPlayers();
        int RemoveBenchmarkBots();
        void AddPlayerSession(Guid playerId, Shared.Models.PlayerSession session);
        bool RemovePlayerSession(Guid playerId);
        Shared.Models.PlayerSession? GetPlayerSession(Guid playerId);
    }

    public class PlayerRepository : IPlayerRepository
    {
        private readonly ConcurrentDictionary<Guid, Shared.Models.PlayerState> _players = new();
        private readonly ConcurrentDictionary<Guid, Shared.Models.PlayerSession> _playerSessions = new();

        public void AddPlayer(Shared.Models.PlayerState player)
        {
            _players[player.Id] = player;
        }

        public bool RemovePlayer(Guid playerId)
        {
            var removed = _players.TryRemove(playerId, out _);
            if (removed)
            {
                _playerSessions.TryRemove(playerId, out _);
            }
            return removed;
        }

        public Shared.Models.PlayerState? GetPlayer(Guid playerId)
        {
            return _players.TryGetValue(playerId, out var player) ? player : null;
        }

        public IReadOnlyCollection<Shared.Models.PlayerState> GetAllPlayers()
        {
            return _players.Values.ToList().AsReadOnly();
        }

        public int GetPlayerCount()
        {
            return _players.Count;
        }

        public int GetRealPlayerCount()
        {
            return _players.Values.Count(p => !p.IsBot);
        }

        public int GetBotCount()
        {
            return _players.Values.Count(p => p.IsBot);
        }

        public int GetActiveBotCount()
        {
            return _players.Values.Count(p => p.IsBot && p.BotBehavior != Shared.Models.BotBehavior.Idle);
        }

        public int RemoveAllNonAdminPlayers()
        {
            var toKick = _players.Values.Where(p => !p.IsAdmin).ToList();
            foreach (var state in toKick)
            {
                _players.TryRemove(state.Id, out _);
                _playerSessions.TryRemove(state.Id, out _);
            }
            return toKick.Count;
        }

        public int RemoveBenchmarkBots()
        {
            var botIds = _players.Where(p => p.Value.IsBot && p.Value.Name.StartsWith("BenchBot_"))
                                .Select(p => p.Key)
                                .ToList();
            
            foreach (var botId in botIds)
            {
                _players.TryRemove(botId, out _);
            }
            
            return botIds.Count;
        }

        public void AddPlayerSession(Guid playerId, Shared.Models.PlayerSession session)
        {
            _playerSessions[playerId] = session;
        }

        public bool RemovePlayerSession(Guid playerId)
        {
            return _playerSessions.TryRemove(playerId, out _);
        }

        public Shared.Models.PlayerSession? GetPlayerSession(Guid playerId)
        {
            return _playerSessions.TryGetValue(playerId, out var session) ? session : null;
        }
    }
}
