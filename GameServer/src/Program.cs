using GameServer.Interfaces;
using GameServer.Services;
using Shared.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddSingleton<IBroadcastService, BroadcastService>();
builder.Services.AddSingleton<IBotCommunicationService, BotCommunicationService>();
builder.Services.AddSingleton<GameServer.UdpGameServer>(provider =>
{
    var playerRepo = provider.GetRequiredService<IPlayerRepository>();
    return new GameServer.UdpGameServer(8081, playerRepo);
});

var app = builder.Build();

// Get services
var playerRepository = app.Services.GetRequiredService<IPlayerRepository>();
var broadcastService = app.Services.GetRequiredService<IBroadcastService>();
var botCommunication = app.Services.GetRequiredService<IBotCommunicationService>();
var udpServer = app.Services.GetRequiredService<GameServer.UdpGameServer>();

// Store current bot state
var currentBots = new List<BotDto>();

// Subscribe to bot updates
botCommunication.BotsUpdated += (bots) =>
{
    currentBots = bots;
    Console.WriteLine($"[GameServer] Received bot update: {bots.Count} bots");
};

// Start UDP server
_ = Task.Run(() => udpServer.StartAsync());

// Start game loop for broadcasting and physics
_ = Task.Run(async () =>
{
    var lastTime = DateTime.UtcNow;
    while (true)
    {
        try
        {
            var currentTime = DateTime.UtcNow;
            var deltaTime = (float)(currentTime - lastTime).TotalSeconds;
            lastTime = currentTime;

            // Update physics
            udpServer.UpdatePhysics(deltaTime);
            
            // Broadcast state
            await broadcastService.BroadcastState(udpServer, playerRepository, currentBots);
            await Task.Delay(50); // 20 FPS
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameServer] Error in game loop: {ex.Message}");
            await Task.Delay(1000);
        }
    }
});

app.UseStaticFiles();

// Health check
app.MapGet("/health", () => Results.Ok(new { 
    status = "ok", 
    service = "game-server",
    udp_port = 8081,
    players = playerRepository.GetPlayerCount(),
    bots = currentBots.Count
}));

// Game server status page
app.MapGet("/", () => Results.Content(GetGameServerStatusPage(), "text/html"));

// API to get current game state
app.MapGet("/api/game-state", () =>
{
    var players = playerRepository.GetAllPlayers();
    var playerDtos = players.Select(p => new PlayerDto(p.Id, p.Name, p.X, p.Y, p.Z, p.IsNPC)).ToList();
    
    return Results.Ok(new GameStateUpdate
    {
        Players = playerDtos,
        Bots = currentBots,
        Timestamp = DateTime.UtcNow,
        Metrics = new ServerMetrics
        {
            PlayerCount = playerRepository.GetPlayerCount(),
            BotCount = currentBots.Count,
            ActiveConnections = udpServer._clients.Count,
            MemoryUsageMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
            Timestamp = DateTime.UtcNow
        }
    });
});

// API to send commands to bot server
app.MapPost("/api/bot-commands", async (BotCommandRequest request) =>
{
    await botCommunication.SendCommandToBotServer(request.Command, request.Parameters);
    return Results.Ok(new { message = $"Command '{request.Command}' sent to bot server" });
});

// Admin endpoints for player management
app.MapPost("/api/players/kick-all", () =>
{
    var kicked = playerRepository.RemoveAllNonAdminPlayers();
    return Results.Ok(new { message = $"Kicked {kicked} players" });
});

app.MapGet("/api/players", () =>
{
    var players = playerRepository.GetAllPlayers();
    return Results.Ok(players.Select(p => new PlayerDto(p.Id, p.Name, p.X, p.Y, p.Z, p.IsNPC)));
});

Console.WriteLine("🎮 Game Server starting...");
Console.WriteLine("📡 UDP Server will be available on port 8081");
Console.WriteLine("🌐 Web API available on http://localhost:8080");

app.Run();

static string GetGameServerStatusPage()
{
    return """
<!DOCTYPE html>
<html>
<head>
    <title>🎮 Game Server Status</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; text-align: center; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 40px; border-radius: 8px; }
        .status { padding: 20px; margin: 20px 0; border-radius: 8px; background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }
        .info { background: #e7f3ff; padding: 20px; margin: 20px 0; border-radius: 8px; border: 1px solid #007bff; }
        h1 { color: #333; }
    </style>
</head>
        <meta charset="UTF-8">
<body>
    <div class="container">
        <h1>🎮 Game Server</h1>
        <div class="status">✅ Game Server is running</div>
        <div class="info">
            <h2>📡 UDP Game Server</h2>
            <p><strong>Port:</strong> 8081</p>
            <p><strong>Protocol:</strong> UDP</p>
            <p>Handles player connections and game state</p>
        </div>
        <div class="info">
            <h2>🤖 Bot Integration</h2>
            <p>Connected to Bot Server for bot management</p>
            <p>Receives real-time bot updates</p>
        </div>
        <div class="info">
            <h2>🔗 API Endpoints</h2>
            <ul style="text-align: left; display: inline-block;">
                <li><code>GET /health</code> - Health check</li>
                <li><code>GET /api/game-state</code> - Current game state</li>
                <li><code>POST /api/bot-commands</code> - Send commands to bot server</li>
                <li><code>GET /api/players</code> - List all players</li>
            </ul>
        </div>
    </div>
</body>
</html>
""";
}
