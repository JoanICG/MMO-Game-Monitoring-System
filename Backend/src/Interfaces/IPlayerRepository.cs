using System.Collections.Concurrent;

namespace Backend.Interfaces
{
    public interface IPlayerRepository
    {
        void AddPlayer(PlayerState player);
        bool RemovePlayer(Guid playerId);
        PlayerState? GetPlayer(Guid playerId);
        IReadOnlyCollection<PlayerState> GetAllPlayers();
        int GetPlayerCount();
        int GetRealPlayerCount();
        int GetBotCount();
        int GetActiveBotCount();
        int RemoveAllNonAdminPlayers();
        int RemoveBenchmarkBots();
        void AddPlayerSession(Guid playerId, PlayerSession session);
        bool RemovePlayerSession(Guid playerId);
        PlayerSession? GetPlayerSession(Guid playerId);
        
        // Legacy compatibility
        void AddPlayer(Guid id, PlayerState player);
        void RemovePlayerLegacy(Guid id);
        IEnumerable<PlayerState> GetAllPlayersLegacy();
        bool UpdatePlayer(Guid id, Action<PlayerState> updateAction);
        int PlayerCount { get; }
    }

    public class PlayerRepository : IPlayerRepository
    {
        private readonly ConcurrentDictionary<Guid, PlayerState> _players = new();
        private readonly ConcurrentDictionary<Guid, PlayerSession> _playerSessions = new();

        public void AddPlayer(PlayerState player)
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

        public PlayerState? GetPlayer(Guid playerId)
        {
            return _players.TryGetValue(playerId, out var player) ? player : null;
        }

        public IReadOnlyCollection<PlayerState> GetAllPlayers()
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
            return _players.Values.Count(p => p.IsBot && p.BotBehavior != BotBehavior.Idle);
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

        public void AddPlayerSession(Guid playerId, PlayerSession session)
        {
            _playerSessions[playerId] = session;
        }

        public bool RemovePlayerSession(Guid playerId)
        {
            return _playerSessions.TryRemove(playerId, out _);
        }

        public PlayerSession? GetPlayerSession(Guid playerId)
        {
            return _playerSessions.TryGetValue(playerId, out var session) ? session : null;
        }

        // Legacy compatibility methods
        public void AddPlayer(Guid id, PlayerState player)
        {
            // Can't modify Id after creation, so create new player with correct Id
            var newPlayer = new PlayerState
            {
                Id = id,
                Name = player.Name,
                X = player.X,
                Y = player.Y,
                Z = player.Z,
                IsBot = player.IsBot,
                IsNPC = player.IsNPC,
                IsAdmin = player.IsAdmin,
                BotBehavior = player.BotBehavior,
                BotSpeed = player.BotSpeed,
                LastUpdate = player.LastUpdate
            };
            AddPlayer(newPlayer);
        }

        public void RemovePlayerLegacy(Guid id)
        {
            RemovePlayer(id);
        }

        public IEnumerable<PlayerState> GetAllPlayersLegacy()
        {
            return _players.Values.ToList();
        }

        public bool UpdatePlayer(Guid id, Action<PlayerState> updateAction)
        {
            if (_players.TryGetValue(id, out var player))
            {
                updateAction(player);
                return true;
            }
            return false;
        }

        public int PlayerCount => _players.Count;
    }
}
