using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Backend;

public class UdpGameServer : IDisposable
{
    private readonly UdpClient _udpServer;
    private readonly ConcurrentDictionary<IPEndPoint, PlayerSession> _clients = new();
    private readonly GameSession _gameSession;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private bool _running = false;
    private Task? _receiveTask;

    public UdpGameServer(int port = 8081)
    {
        _udpServer = new UdpClient(port);
        _gameSession = new GameSession();
        Console.WriteLine($"[UdpGameServer] Started on port {port}");
    }

    public async Task StartAsync()
    {
        _running = true;
        _receiveTask = ReceiveLoop();
        
        // Start game loop for broadcasting state
        _ = Task.Run(GameLoop);
        
        await _receiveTask;
    }

    private async Task ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                var result = await _udpServer.ReceiveAsync();
                var endpoint = result.RemoteEndPoint;
                var data = result.Buffer;
                
                await HandleClientMessage(endpoint, data);
            }
            catch (Exception ex) when (_running)
            {
                Console.WriteLine($"[UdpGameServer] Receive error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientMessage(IPEndPoint endpoint, byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("op", out var opProp)) return;
            var op = opProp.GetString();

            // Get or create client session
            if (!_clients.TryGetValue(endpoint, out var session))
            {
                session = new PlayerSession(endpoint);
                _clients[endpoint] = session;
            }

            await _gameSession.HandleUdpMessage(session, doc, this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UdpGameServer] Message handling error: {ex.Message}");
        }
    }

    public async Task SendToClient(IPEndPoint endpoint, object message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, _json);
            var data = Encoding.UTF8.GetBytes(json);
            await _udpServer.SendAsync(data, endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UdpGameServer] Send error to {endpoint}: {ex.Message}");
        }
    }

    public async Task BroadcastToAll(object message)
    {
        var json = JsonSerializer.Serialize(message, _json);
        var data = Encoding.UTF8.GetBytes(json);
        
        var tasks = _clients.Keys.Select(async endpoint =>
        {
            try
            {
                await _udpServer.SendAsync(data, endpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpGameServer] Broadcast error to {endpoint}: {ex.Message}");
            }
        });
        
        await Task.WhenAll(tasks);
    }

    private async Task GameLoop()
    {
        while (_running)
        {
            try
            {
                await _gameSession.BroadcastStateUdp(this);
                await Task.Delay(50); // 20Hz update rate
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpGameServer] Game loop error: {ex.Message}");
            }
        }
    }

    public void RemoveClient(IPEndPoint endpoint)
    {
        _clients.TryRemove(endpoint, out _);
    }

    public void Dispose()
    {
        _running = false;
        _udpServer?.Dispose();
    }
}

public class PlayerSession
{
    public IPEndPoint EndPoint { get; }
    public Guid PlayerId { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public uint LastSequenceReceived { get; set; }

    public PlayerSession(IPEndPoint endPoint)
    {
        EndPoint = endPoint;
        LastHeartbeat = DateTime.UtcNow;
    }
}
