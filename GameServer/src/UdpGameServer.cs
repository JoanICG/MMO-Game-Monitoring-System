using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Shared.Models;

namespace GameServer
{
    public class UdpGameServer
    {
        private readonly UdpClient _udpClient;
        private readonly int _port;
        private bool _isRunning;
        private readonly GameServer.Interfaces.IPlayerRepository _playerRepository;

        public readonly Dictionary<string, PlayerSession> _clients = new();
        private readonly JsonSerializerOptions _jsonOptions;

        public UdpGameServer(int port, GameServer.Interfaces.IPlayerRepository playerRepository)
        {
            _port = port;
            _playerRepository = playerRepository;
            _udpClient = new UdpClient(port);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            Console.WriteLine($"[UdpGameServer] UDP Game Server initialized on port {port}");
        }

        public async Task StartAsync()
        {
            _isRunning = true;
            Console.WriteLine($"[UdpGameServer] UDP Server listening on port {_port}");

            while (_isRunning)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    _ = Task.Run(() => HandleClientMessage(result.Buffer, result.RemoteEndPoint));
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine($"[UdpGameServer] Error receiving UDP message: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleClientMessage(byte[] data, IPEndPoint endPoint)
        {
            try
            {
                var message = Encoding.UTF8.GetString(data);
                var doc = JsonDocument.Parse(message);
                
                if (!doc.RootElement.TryGetProperty("op", out var opProp))
                {
                    Console.WriteLine("[UdpGameServer] Message missing 'op' property");
                    return;
                }

                var operation = opProp.GetString();
                var clientKey = endPoint.ToString();

                Console.WriteLine($"[UdpGameServer] Received operation '{operation}' from {clientKey}");

                switch (operation)
                {
                    case "join":
                        await HandleJoin(doc, endPoint);
                        break;
                    case "input":
                        await HandleInput(doc, endPoint);
                        break;
                    case "ping":
                        await HandlePing(endPoint);
                        break;
                    default:
                        Console.WriteLine($"[UdpGameServer] Unknown operation: {operation}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpGameServer] Error handling client message: {ex.Message}");
            }
        }

        private async Task HandleJoin(JsonDocument doc, IPEndPoint endPoint)
        {
            var playerName = doc.RootElement.TryGetProperty("name", out var nameProp) 
                ? nameProp.GetString() ?? $"Player{Random.Shared.Next(1000, 9999)}" 
                : $"Player{Random.Shared.Next(1000, 9999)}";

            var playerId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var clientKey = endPoint.ToString();

            var session = new PlayerSession
            {
                Id = sessionId,
                PlayerId = playerId,
                ConnectedAt = DateTime.UtcNow,
                LastInputTime = DateTime.UtcNow,
                EndPoint = endPoint,
                IsConnected = true
            };

            _clients[clientKey] = session;

            // Add player to repository so they appear in the game
            var playerState = new PlayerState
            {
                Id = playerId,
                Name = playerName,
                X = 0,
                Y = 0,
                Z = 0,
                LastUpdate = DateTime.UtcNow,
                IsBot = false,
                IsNPC = false,
                IsAdmin = false
            };
            _playerRepository.AddPlayer(playerState);

            var joinResponse = new
            {
                op = "joined",
                success = true,
                playerId = playerId.ToString(),
                name = playerName
            };

            await SendToClient(endPoint, joinResponse);
            Console.WriteLine($"[UdpGameServer] Player {playerName} joined from {endPoint}");
        }

        private async Task HandleInput(JsonDocument doc, IPEndPoint endPoint)
        {
            var clientKey = endPoint.ToString();
            if (!_clients.TryGetValue(clientKey, out var session))
            {
                Console.WriteLine($"[UdpGameServer] Input from unknown client: {endPoint}");
                return;
            }

            session.LastInputTime = DateTime.UtcNow;

            // Get player from repository
            var player = _playerRepository.GetPlayer(session.PlayerId);
            if (player == null)
            {
                Console.WriteLine($"[UdpGameServer] Player not found for session: {session.PlayerId}");
                return;
            }

            // Parse input data - client sends their predicted position
            var x = doc.RootElement.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : player.X;
            var y = doc.RootElement.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : player.Y;
            var z = doc.RootElement.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : player.Z;

            // Server reconciliation: validate and correct position
            // Boundary checking
            x = Math.Clamp(x, -50f, 50f);
            y = Math.Clamp(y, 0f, 20f); // Prevent flying too high
            z = Math.Clamp(z, -50f, 50f);

            // Update player position (server-authoritative)
            player.X = x;
            player.Y = y;
            player.Z = z;
            player.LastUpdate = DateTime.UtcNow;

            var response = new { op = "input_ack" };
            await SendToClient(endPoint, response);
        }

        private async Task HandlePing(IPEndPoint endPoint)
        {
            var response = new { op = "pong", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            await SendToClient(endPoint, response);
        }

        public async Task SendToClient(EndPoint endPoint, object message)
        {
            try
            {
                var json = JsonSerializer.Serialize(message, _jsonOptions);
                var data = Encoding.UTF8.GetBytes(json);
                await _udpClient.SendAsync(data, (IPEndPoint)endPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpGameServer] Error sending to client {endPoint}: {ex.Message}");
            }
        }

        public async Task BroadcastToAll(object message)
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            var data = Encoding.UTF8.GetBytes(json);

            var tasks = _clients.Values
                .Where(session => session.IsConnected)
                .Select(async session => await _udpClient.SendAsync(data, (IPEndPoint)session.EndPoint));

            await Task.WhenAll(tasks);
        }

        public void UpdatePhysics(float deltaTime)
        {
            // Physics is now client-side only
            // Server only validates positions and broadcasts state
            var players = _playerRepository.GetAllPlayers();

            foreach (var player in players)
            {
                // Server-side validation only - ensure positions stay within bounds
                player.X = Math.Clamp(player.X, -50f, 50f);
                player.Y = Math.Clamp(player.Y, 0f, 20f);
                player.Z = Math.Clamp(player.Z, -50f, 50f);
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _udpClient?.Close();
            Console.WriteLine("[UdpGameServer] UDP Server stopped");
        }

        public void Dispose()
        {
            Stop();
            _udpClient?.Dispose();
        }
    }
}
