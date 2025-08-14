using Backend;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();
app.UseStaticFiles(); // Para servir archivos estáticos

var session = new GameSession();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Panel de administración
app.MapGet("/admin", async ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(GetAdminPanel());
});

// WebSocket para clientes normales
app.Map("/ws", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Expected WebSocket");
        return;
    }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("[Program] WebSocket accepted /ws (client)");
    await session.HandleAsync(socket, false); // false = not admin
});

// WebSocket para administradores
app.Map("/admin-ws", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Expected WebSocket");
        return;
    }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("[Program] WebSocket accepted /admin-ws (admin)");
    await session.HandleAsync(socket, true); // true = admin
});

static string GetAdminPanel()
{
    return """
<!DOCTYPE html>
<html>
<head>
    <title>MMO Admin Panel</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        .status { padding: 10px; margin: 10px 0; border-radius: 4px; }
        .connected { background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }
        .disconnected { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
        .players { margin: 20px 0; }
        .player { background: #e9ecef; padding: 10px; margin: 5px 0; border-radius: 4px; display: flex; justify-content: space-between; align-items: center; }
        .controls { margin: 20px 0; }
        .btn { padding: 8px 16px; margin: 5px; border: none; border-radius: 4px; cursor: pointer; }
        .btn-primary { background: #007bff; color: white; }
        .btn-danger { background: #dc3545; color: white; }
        .btn-success { background: #28a745; color: white; }
        .input { padding: 8px; margin: 5px; border: 1px solid #ddd; border-radius: 4px; }
        .logs { background: #f8f9fa; padding: 10px; margin: 10px 0; max-height: 200px; overflow-y: auto; font-family: monospace; font-size: 12px; border: 1px solid #dee2e6; border-radius: 4px; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🎮 MMO Admin Panel</h1>
        
        <div id="status" class="status disconnected">
            Disconnected
        </div>
        
        <div class="controls">
            <h3>Spawn NPC</h3>
            <input type="text" id="npcName" class="input" placeholder="NPC Name" value="Bot">
            <input type="number" id="npcX" class="input" placeholder="X" value="0" step="0.1">
            <input type="number" id="npcY" class="input" placeholder="Y" value="0" step="0.1">
            <input type="number" id="npcZ" class="input" placeholder="Z" value="0" step="0.1">
            <button class="btn btn-success" onclick="spawnNPC()">Spawn NPC</button>
            
            <h3>Global Commands</h3>
            <button class="btn btn-primary" onclick="teleportAll()">Teleport All to Origin</button>
            <button class="btn btn-danger" onclick="kickAll()">Kick All Players</button>
        </div>
        
        <div class="players">
            <h3>Connected Players</h3>
            <div id="playerList"></div>
        </div>
        
        <div class="logs" id="logs"></div>
    </div>

    <script>
        let ws = null;
        let players = {};

        function connect() {
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            ws = new WebSocket(`${protocol}//${window.location.host}/admin-ws`);
            
            ws.onopen = () => {
                updateStatus('Connected', true);
                addLog('Connected to admin WebSocket');
                // Join as admin
                ws.send(JSON.stringify({op: 'admin_join', name: 'Admin'}));
            };
            
            ws.onclose = () => {
                updateStatus('Disconnected', false);
                addLog('Disconnected from admin WebSocket');
                setTimeout(connect, 3000); // Reconnect
            };
            
            ws.onmessage = (event) => {
                const msg = JSON.parse(event.data);
                addLog(`RX: ${event.data}`);
                
                if (msg.op === 'state') {
                    players = {};
                    msg.players.forEach(p => {
                        players[p.id] = p;
                    });
                    updatePlayerList();
                }
            };
        }

        function updateStatus(text, connected) {
            const status = document.getElementById('status');
            status.textContent = text;
            status.className = `status ${connected ? 'connected' : 'disconnected'}`;
        }

        function addLog(text) {
            const logs = document.getElementById('logs');
            const time = new Date().toLocaleTimeString();
            logs.innerHTML += `${time}: ${text}\n`;
            logs.scrollTop = logs.scrollHeight;
        }

        function updatePlayerList() {
            const list = document.getElementById('playerList');
            list.innerHTML = '';
            
            Object.values(players).forEach(player => {
                const div = document.createElement('div');
                div.className = 'player';
                div.innerHTML = `
                    <span><strong>${player.name}</strong> (${player.id.substring(0,8)}...) - Position: (${player.x.toFixed(1)}, ${player.y.toFixed(1)}, ${player.z.toFixed(1)})</span>
                    <div>
                        <button class="btn btn-primary" onclick="teleportPlayer('${player.id}')">Teleport</button>
                        <button class="btn btn-danger" onclick="kickPlayer('${player.id}')">Kick</button>
                    </div>
                `;
                list.appendChild(div);
            });
        }

        function spawnNPC() {
            const name = document.getElementById('npcName').value || 'Bot';
            const x = parseFloat(document.getElementById('npcX').value) || 0;
            const y = parseFloat(document.getElementById('npcY').value) || 0;
            const z = parseFloat(document.getElementById('npcZ').value) || 0;
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_spawn_npc',
                    name: name,
                    x: x,
                    y: y,
                    z: z
                }));
                addLog(`Spawning NPC: ${name} at (${x}, ${y}, ${z})`);
            }
        }

        function teleportPlayer(playerId) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_teleport',
                    playerId: playerId,
                    x: 0,
                    y: 0,
                    z: 0
                }));
                addLog(`Teleporting player ${playerId} to origin`);
            }
        }

        function kickPlayer(playerId) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_kick',
                    playerId: playerId
                }));
                addLog(`Kicking player ${playerId}`);
            }
        }

        function teleportAll() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_teleport_all',
                    x: 0,
                    y: 0,
                    z: 0
                }));
                addLog('Teleporting all players to origin');
            }
        }

        function kickAll() {
            if (confirm('Are you sure you want to kick all players?')) {
                if (ws && ws.readyState === WebSocket.OPEN) {
                    ws.send(JSON.stringify({op: 'admin_kick_all'}));
                    addLog('Kicking all players');
                }
            }
        }

        // Connect on page load
        connect();
    </script>
</body>
</html>
""";
}

app.Run();
