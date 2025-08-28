using Shared.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<BotServer.BotManagementSystem>(provider => BotServer.BotManagementSystem.Instance);

var app = builder.Build();

// Get services
var botManager = app.Services.GetRequiredService<BotServer.BotManagementSystem>();

app.UseStaticFiles();

// Health check
app.MapGet("/health", () => Results.Ok(new { 
    status = "ok", 
    service = "bot-server",
    containers = botManager.TotalContainers,
    bots = botManager.TotalBots,
    active_bots = botManager.TotalActiveBots
}));

// Bot server status page
app.MapGet("/", () => Results.Content(GetBotServerStatusPage(), "text/html"));

// API to get all bots (for Game Server)
app.MapGet("/api/bots", () =>
{
    var bots = botManager.GetAllBots();
    return Results.Ok(bots.Select(b => b.ToDto()));
});

// API to get system stats
app.MapGet("/api/stats", () =>
{
    return Results.Ok(botManager.GetSystemStats());
});

// Container management
app.MapPost("/api/containers", (CreateContainerRequest request) =>
{
    var container = botManager.CreateContainer(request.Name, request.MaxBots);
    if (container != null)
    {
        return Results.Ok(new { containerId = container.Id, message = $"Container '{request.Name}' created" });
    }
    return Results.BadRequest(new { message = "Failed to create container" });
});

app.MapDelete("/api/containers/{containerId:guid}", (Guid containerId) =>
{
    if (botManager.RemoveContainer(containerId))
    {
        return Results.Ok(new { message = "Container removed" });
    }
    return Results.NotFound(new { message = "Container not found" });
});

app.MapGet("/api/containers", () =>
{
    var containers = botManager.GetAllContainers();
    return Results.Ok(containers.Select(c => new BotContainerInfo
    {
        Id = c.Id,
        Name = c.Name,
        MaxBots = c.MaxBots,
        CurrentBots = c.Bots.Count,
        ActiveBots = c.ActiveBotsCount,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt
    }));
});

// Bot management in containers
app.MapPost("/api/containers/{containerId:guid}/bots", (Guid containerId, SpawnBotsRequest request) =>
{
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    int spawned = 0;
    for (int i = 0; i < request.Count; i++)
    {
        if (container.AddBot($"Bot_{DateTime.Now:HHmmss}_{i + 1}"))
        {
            spawned++;
        }
        else
        {
            break; // Container full
        }
    }

    return Results.Ok(new { spawned, message = $"Spawned {spawned} bots" });
});

app.MapDelete("/api/containers/{containerId:guid}/bots", (Guid containerId) =>
{
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    container.RemoveAllBots();
    return Results.Ok(new { message = "All bots removed from container" });
});

app.MapPost("/api/containers/{containerId:guid}/pause", (Guid containerId) =>
{
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    container.PauseAllBots();
    return Results.Ok(new { message = "Container bots paused" });
});

app.MapPost("/api/containers/{containerId:guid}/resume", (Guid containerId) =>
{
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    container.ResumeAllBots();
    return Results.Ok(new { message = "Container bots resumed" });
});

// Global bot operations
app.MapPost("/api/bots/pause-all", () =>
{
    botManager.PauseAllBots();
    return Results.Ok(new { message = "All bots paused" });
});

app.MapPost("/api/bots/resume-all", () =>
{
    botManager.ResumeAllBots();
    return Results.Ok(new { message = "All bots resumed" });
});

app.MapDelete("/api/bots", () =>
{
    botManager.RemoveAllBots();
    return Results.Ok(new { message = "All bots removed" });
});

app.MapDelete("/api/containers", () =>
{
    botManager.RemoveAllContainers();
    return Results.Ok(new { message = "All containers removed" });
});

// Command processor (for commands from Game Server)
app.MapPost("/api/commands", async (BotCommandRequest request) =>
{
    Console.WriteLine($"[BotServer] Received command: {request.Command}");

    return request.Command switch
    {
        "pause_all" => HandlePauseAll(),
        "resume_all" => HandleResumeAll(),
        "create_container" => HandleCreateContainer(request.Parameters),
        "spawn_bots" => HandleSpawnBots(request.Parameters),
        _ => Results.BadRequest(new { message = $"Unknown command: {request.Command}" })
    };

    IResult HandlePauseAll()
    {
        botManager.PauseAllBots();
        return Results.Ok(new { message = "All bots paused" });
    }

    IResult HandleResumeAll()
    {
        botManager.ResumeAllBots();
        return Results.Ok(new { message = "All bots resumed" });
    }

    IResult HandleCreateContainer(object? parameters)
    {
        // Implementation for create container command
        return Results.Ok(new { message = "Container creation not implemented in command processor" });
    }

    IResult HandleSpawnBots(object? parameters)
    {
        // Implementation for spawn bots command
        return Results.Ok(new { message = "Bot spawning not implemented in command processor" });
    }
});

Console.WriteLine("🤖 Bot Server starting...");
Console.WriteLine("🌐 Bot API available on http://localhost:8082");
Console.WriteLine("📊 Bot Management System initialized");

app.Run();

static string GetBotServerStatusPage()
{
    return """
<!DOCTYPE html>
<html>
<head>
    <title>🤖 Bot Server Status</title>
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
        <h1>🤖 Bot Server</h1>
        <div class="status">✅ Bot Server is running</div>
        <div class="info">
            <h2>🏗️ Bot Management</h2>
            <p>Manages bot containers and bot lifecycle</p>
            <p>Provides real-time bot position updates</p>
        </div>
        <div class="info">
            <h2>🔗 API Endpoints</h2>
            <ul style="text-align: left; display: inline-block;">
                <li><code>GET /api/bots</code> - Get all bots (for Game Server)</li>
                <li><code>GET /api/stats</code> - Bot system statistics</li>
                <li><code>POST /api/containers</code> - Create bot container</li>
                <li><code>POST /api/containers/{id}/bots</code> - Spawn bots in container</li>
                <li><code>POST /api/commands</code> - Process commands from Game Server</li>
            </ul>
        </div>
        <div class="info">
            <h2>🔄 Integration</h2>
            <p>Communicates with Game Server for coordinated bot management</p>
            <p>Sends regular bot updates to Game Server</p>
        </div>
    </div>
</body>
</html>
""";
}
