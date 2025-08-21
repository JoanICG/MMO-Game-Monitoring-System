using System.Text.Json;
using Backend.Interfaces;

namespace Backend.Services
{
    public interface IInputHandler
    {
        Task HandleJoin(PlayerSession session, JsonDocument doc, UdpGameServer server, IPlayerRepository playerRepository);
        Task HandleInput(PlayerSession session, JsonDocument doc, UdpGameServer server, IPlayerRepository playerRepository);
        Task ProcessPlayerMovement(PlayerState player, float inputX, float inputY, float speed, uint sequence);
        bool ValidateInput(PlayerState player, uint sequence, PlayerSession session);
        
        // Legacy compatibility
        Task HandleJoinAsync(PlayerSession session, JsonDocument doc, object server);
        Task HandleInputAsync(PlayerSession session, JsonDocument doc, object server);
        Task HandleHeartbeatAsync(PlayerSession session);
    }

    public class InputHandler : IInputHandler
    {
        private readonly IPlayerRepository? _playerRepository;
        private readonly IBroadcastService? _broadcastService;
        private readonly BotManagementSystem? _botManager;

        public InputHandler()
        {
            // Default constructor for dependency injection
        }

        public InputHandler(IPlayerRepository playerRepository, IBroadcastService broadcastService)
        {
            _playerRepository = playerRepository;
            _broadcastService = broadcastService;
            _botManager = BotManagementSystem.Instance;
        }

        public async Task HandleJoin(PlayerSession session, JsonDocument doc, UdpGameServer server, IPlayerRepository playerRepository)
        {
            var name = doc.RootElement.TryGetProperty("name", out var nameProp) ? 
                       nameProp.GetString() ?? "Player" : "Player";
            
            var player = new PlayerState { Name = name[..Math.Min(name.Length, 16)] };
            session.PlayerId = player.Id;
            
            playerRepository.AddPlayer(player);
            playerRepository.AddPlayerSession(player.Id, session);
            
            Console.WriteLine($"[InputHandler] UDP JOIN id={player.Id} name={player.Name} endpoint={session.EndPoint}");
            
            // Send join acknowledgment
            await server.SendToClient(session.EndPoint, new JoinAck("join_ack", player.Id));
        }

        public async Task HandleInput(PlayerSession session, JsonDocument doc, UdpGameServer server, IPlayerRepository playerRepository)
        {
            var player = playerRepository.GetPlayer(session.PlayerId);
            if (player == null) return;

            // Extract sequence number for duplicate detection
            var sequence = doc.RootElement.TryGetProperty("seq", out var seqP) ? seqP.GetUInt32() : 0;
            
            // Validate input
            if (!ValidateInput(player, sequence, session)) return;

            // Extract input data
            var inputX = doc.RootElement.TryGetProperty("x", out var ixP) ? ixP.GetSingle() : 0f;
            var inputY = doc.RootElement.TryGetProperty("y", out var iyP) ? iyP.GetSingle() : 0f;
            var speed = doc.RootElement.TryGetProperty("speed", out var speedP) ? speedP.GetSingle() : 5f;

            // Process movement
            await ProcessPlayerMovement(player, inputX, inputY, speed, sequence);
        }

        public async Task ProcessPlayerMovement(PlayerState player, float inputX, float inputY, float speed, uint sequence)
        {
            // Server authority check
            if (player.ServerAuthorityUntil.HasValue && DateTime.UtcNow < player.ServerAuthorityUntil.Value)
            {
                Console.WriteLine($"[InputHandler] INPUT IGNORED (SERVER AUTHORITY) id={player.Id}");
                return;
            }

            // Apply server-side movement validation
            var deltaTime = 0.05f; // 20Hz server tick
            var inputMagnitude = Math.Sqrt(inputX * inputX + inputY * inputY);
            if (inputMagnitude > 1f)
            {
                inputX /= (float)inputMagnitude;
                inputY /= (float)inputMagnitude;
            }

            // Apply movement with bounds checking
            var deltaX = inputX * speed * deltaTime;
            var deltaZ = inputY * speed * deltaTime;

            // Optional: Add world bounds
            const float WORLD_SIZE = 1000f;
            var newX = Math.Clamp(player.X + deltaX, -WORLD_SIZE, WORLD_SIZE);
            var newZ = Math.Clamp(player.Z + deltaZ, -WORLD_SIZE, WORLD_SIZE);

            player.X = newX;
            player.Z = newZ;
            player.LastUpdate = DateTime.UtcNow;

            Console.WriteLine($"[InputHandler] INPUT PROCESSED seq={sequence} id={player.Id} pos=({player.X:F2},{player.Y:F2},{player.Z:F2})");
        }

        public bool ValidateInput(PlayerState player, uint sequence, PlayerSession session)
        {
            // Skip if this is an old packet (simple duplicate detection)
            if (sequence <= session.LastSequenceReceived) 
            {
                Console.WriteLine($"[InputHandler] INPUT IGNORED (OLD SEQUENCE) id={player.Id} seq={sequence} <= {session.LastSequenceReceived}");
                return false;
            }
            
            session.LastSequenceReceived = sequence;

            // Rate limiting: max 100 inputs per second per player
            var now = DateTime.UtcNow;
            if (session.LastInputTime.HasValue && (now - session.LastInputTime.Value).TotalMilliseconds < 10)
            {
                Console.WriteLine($"[InputHandler] INPUT IGNORED (RATE LIMITED) id={player.Id}");
                return false;
            }
            
            session.LastInputTime = now;
            return true;
        }

        // Legacy compatibility methods
        public async Task HandleJoinAsync(PlayerSession session, JsonDocument doc, object server)
        {
            if (_playerRepository == null || _broadcastService == null || _botManager == null)
            {
                Console.WriteLine("[InputHandler] Legacy method called but dependencies not available");
                return;
            }

            if (!doc.RootElement.TryGetProperty("name", out var nameProp))
            {
                Console.WriteLine("[InputHandler] Join message missing 'name' property");
                return;
            }

            var playerName = nameProp.GetString() ?? $"Player{Random.Shared.Next(1000, 9999)}";
            var playerId = session.Id;

            Console.WriteLine($"[InputHandler] Player {playerName} (ID: {playerId}) joining via UDP");

            var playerState = new PlayerState
            {
                Id = playerId,
                Name = playerName,
                X = 0,
                Y = 0,
                Z = 0,
                LastUpdate = DateTime.UtcNow,
                IsNPC = false
            };

            _playerRepository.AddPlayer(playerId, playerState);

            var joinResponse = new
            {
                op = "joined",
                success = true,
                playerId = playerId.ToString(),
                name = playerName
            };

            var responseJson = JsonSerializer.Serialize(joinResponse, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });

            // Send response (would need server interface)
            Console.WriteLine($"[InputHandler] Player {playerName} joined successfully");

            // Broadcast updated state
            await _broadcastService.BroadcastStateAsync(
                _playerRepository.GetAllPlayersLegacy(), 
                _botManager.GetAllActiveBots(), 
                server);
        }

        public async Task HandleInputAsync(PlayerSession session, JsonDocument doc, object server)
        {
            if (_playerRepository == null || _broadcastService == null || _botManager == null)
            {
                Console.WriteLine("[InputHandler] Legacy method called but dependencies not available");
                return;
            }

            if (!doc.RootElement.TryGetProperty("input", out var inputProp))
            {
                return;
            }

            var playerId = session.Id;
            var player = _playerRepository.GetPlayer(playerId);
            if (player == null) return;

            // Extract movement input
            var hasMovement = false;
            float deltaX = 0, deltaZ = 0;

            if (inputProp.TryGetProperty("movement", out var movementProp))
            {
                if (movementProp.TryGetProperty("x", out var xProp) && xProp.TryGetSingle(out deltaX))
                    hasMovement = true;
                if (movementProp.TryGetProperty("z", out var zProp) && zProp.TryGetSingle(out deltaZ))
                    hasMovement = true;
            }

            if (hasMovement)
            {
                const float moveSpeed = 5.0f;
                const float deltaTime = 0.016f; // ~60 FPS

                _playerRepository.UpdatePlayer(playerId, p =>
                {
                    p.X += deltaX * moveSpeed * deltaTime;
                    p.Z += deltaZ * moveSpeed * deltaTime;
                    p.LastUpdate = DateTime.UtcNow;
                });

                // Broadcast updated state
                await _broadcastService.BroadcastStateAsync(
                    _playerRepository.GetAllPlayersLegacy(), 
                    _botManager.GetAllActiveBots(), 
                    server);
            }
        }

        public async Task HandleHeartbeatAsync(PlayerSession session)
        {
            session.LastHeartbeat = DateTime.UtcNow;
            // Heartbeat doesn't require broadcast
            await Task.CompletedTask;
        }
    }
}
