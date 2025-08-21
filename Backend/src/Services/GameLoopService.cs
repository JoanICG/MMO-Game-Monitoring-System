using Backend.Interfaces;

namespace Backend.Services
{
    public interface IGameLoopService : IDisposable
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
    }

    public class GameLoopService : IGameLoopService
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IBroadcastService _broadcastService;
        private readonly BotManagementSystem _botManager;
        private Timer? _gameLoopTimer;
        private bool _disposed = false;
        private bool _isRunning = false;

        private const int BOT_UPDATE_INTERVAL_MS = 100; // 10 FPS for bot updates

        public bool IsRunning => _isRunning;

        public GameLoopService(IPlayerRepository playerRepository, IBroadcastService broadcastService)
        {
            _playerRepository = playerRepository;
            _broadcastService = broadcastService;
            _botManager = BotManagementSystem.Instance;
        }

        public void Start()
        {
            if (_isRunning) return;

            _gameLoopTimer = new Timer(
                GameLoopTick, 
                null, 
                TimeSpan.FromMilliseconds(BOT_UPDATE_INTERVAL_MS), 
                TimeSpan.FromMilliseconds(BOT_UPDATE_INTERVAL_MS)
            );

            _isRunning = true;
            Console.WriteLine("[GameLoopService] Game loop started (bot updates every 100ms)");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _gameLoopTimer?.Dispose();
            _gameLoopTimer = null;
            _isRunning = false;
            Console.WriteLine("[GameLoopService] Game loop stopped");
        }

        private void GameLoopTick(object? state)
        {
            if (_disposed) return;

            try
            {
                // Update legacy bots (if any)
                UpdateLegacyBots();

                // Bot Management System handles its own updates
                // No need to broadcast here as it's handled by individual actions
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameLoopService] Error in game loop: {ex.Message}");
            }
        }

        private void UpdateLegacyBots()
        {
            var players = _playerRepository.GetAllPlayers();
            var botsToUpdate = players.Where(p => p.IsBot && p.BotBehavior != BotBehavior.Idle).ToList();
            
            if (botsToUpdate.Count == 0) return;

            // Performance optimization: limit concurrent bot updates for large counts
            const int MAX_BOTS_PER_UPDATE = 200;
            var botsThisUpdate = botsToUpdate.Count > MAX_BOTS_PER_UPDATE ? 
                botsToUpdate.Take(MAX_BOTS_PER_UPDATE).ToList() : 
                botsToUpdate;

            bool anyBotMoved = false;
            int updatedCount = 0;

            foreach (var botState in botsThisUpdate)
            {
                if (UpdateSingleBot(botState))
                {
                    anyBotMoved = true;
                    updatedCount++;
                }
            }

            // Log performance metrics occasionally
            if (DateTime.UtcNow.Second % 30 == 0 && DateTime.UtcNow.Millisecond < 200)
            {
                Console.WriteLine($"[GameLoopService] Updated {updatedCount}/{botsThisUpdate.Count} legacy bots");
            }
        }

        private bool UpdateSingleBot(PlayerState botState)
        {
            var originalX = botState.X;
            var originalZ = botState.Z;

            switch (botState.BotBehavior)
            {
                case BotBehavior.Random:
                    UpdateRandomMovementBot(botState);
                    break;
                case BotBehavior.Follow:
                    UpdateFollowBot(botState);
                    break;
                case BotBehavior.Patrol:
                    UpdatePatrolBot(botState);
                    break;
            }

            // Check if bot actually moved
            const float epsilon = 0.001f;
            return Math.Abs(botState.X - originalX) > epsilon || Math.Abs(botState.Z - originalZ) > epsilon;
        }

        private void UpdateRandomMovementBot(PlayerState botState)
        {
            const float deltaTime = 0.1f; // 100ms update interval
            const float speed = 3.0f;
            const float mapSize = 50.0f;

            // Simple random movement
            var angle = Random.Shared.NextSingle() * 2 * Math.PI;
            var moveX = (float)Math.Cos(angle) * speed * deltaTime;
            var moveZ = (float)Math.Sin(angle) * speed * deltaTime;

            // Apply movement with boundary checks
            botState.X = Math.Clamp(botState.X + moveX, -mapSize, mapSize);
            botState.Z = Math.Clamp(botState.Z + moveZ, -mapSize, mapSize);
            botState.LastUpdate = DateTime.UtcNow;
        }

        private void UpdateFollowBot(PlayerState botState)
        {
            // Find nearest player to follow
            var players = _playerRepository.GetAllPlayers().Where(p => !p.IsBot).ToList();
            if (players.Count == 0) return;

            var nearest = players.OrderBy(p => 
                Math.Sqrt(Math.Pow(p.X - botState.X, 2) + Math.Pow(p.Z - botState.Z, 2))
            ).First();

            // Move towards the nearest player
            const float deltaTime = 0.1f;
            const float speed = 4.0f;

            var dirX = nearest.X - botState.X;
            var dirZ = nearest.Z - botState.Z;
            var distance = Math.Sqrt(dirX * dirX + dirZ * dirZ);

            if (distance > 1.0f) // Don't get too close
            {
                dirX /= (float)distance;
                dirZ /= (float)distance;

                botState.X += dirX * speed * deltaTime;
                botState.Z += dirZ * speed * deltaTime;
                botState.LastUpdate = DateTime.UtcNow;
            }
        }

        private void UpdatePatrolBot(PlayerState botState)
        {
            // Simple patrol behavior - move in a square pattern
            const float deltaTime = 0.1f;
            const float speed = 2.0f;
            const float patrolSize = 10.0f;

            // Use bot's current position as patrol center if no waypoints set
            var centerX = botState.PatrolWaypoints.Count > 0 ? 
                botState.PatrolWaypoints.Average(w => w.X) : botState.X;
            var centerZ = botState.PatrolWaypoints.Count > 0 ? 
                botState.PatrolWaypoints.Average(w => w.Z) : botState.Z;

            // Calculate patrol position based on time
            var time = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond) % 40; // 40 second cycle
            var phase = time / 10.0f; // 4 phases of 10 seconds each

            float targetX, targetZ;
            switch ((int)phase)
            {
                case 0: // Move right
                    targetX = centerX + patrolSize;
                    targetZ = centerZ;
                    break;
                case 1: // Move down
                    targetX = centerX + patrolSize;
                    targetZ = centerZ + patrolSize;
                    break;
                case 2: // Move left
                    targetX = centerX;
                    targetZ = centerZ + patrolSize;
                    break;
                default: // Move up
                    targetX = centerX;
                    targetZ = centerZ;
                    break;
            }

            // Move towards target
            var dirX = targetX - botState.X;
            var dirZ = targetZ - botState.Z;
            var distance = Math.Sqrt(dirX * dirX + dirZ * dirZ);

            if (distance > 0.1f)
            {
                dirX /= (float)distance;
                dirZ /= (float)distance;

                botState.X += dirX * speed * deltaTime;
                botState.Z += dirZ * speed * deltaTime;
                botState.LastUpdate = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Stop();
            _disposed = true;
        }
    }
}
