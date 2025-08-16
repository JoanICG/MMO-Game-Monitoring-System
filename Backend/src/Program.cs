using Backend;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();
app.UseStaticFiles(); // Para servir archivos estáticos

var session = new GameSession();
var udpServer = new UdpGameServer(8081); // UDP on port 8081

// Start UDP server
_ = Task.Run(() => udpServer.StartAsync());

app.MapGet("/health", () => Results.Ok(new { status = "ok", websocket_port = 8080, udp_port = 8081 }));

// Panel de administración
app.MapGet("/admin", async ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(GetAdminPanel());
});

// WebSocket for compatibility (clients should prefer UDP)
app.Map("/ws", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Expected WebSocket");
        return;
    }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("[Program] WebSocket accepted /ws (client) - Consider upgrading to UDP");
    await session.HandleAsync(socket, false); // false = not admin
});

// WebSocket for admin
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
    <title>🤖 MMO Admin Panel - Bot Control</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        .status { padding: 10px; margin: 10px 0; border-radius: 4px; }
        .connected { background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }
        .disconnected { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
        .section { margin: 20px 0; padding: 15px; border: 1px solid #dee2e6; border-radius: 8px; }
        .bot-section { background: #e7f3ff; }
        .player { background: #e9ecef; padding: 10px; margin: 5px 0; border-radius: 4px; display: flex; justify-content: space-between; align-items: center; }
        .bot { background: #cce5ff; border-left: 4px solid #007bff; }
        .controls { margin: 20px 0; }
        .btn { padding: 8px 16px; margin: 5px; border: none; border-radius: 4px; cursor: pointer; font-size: 12px; }
        .btn-primary { background: #007bff; color: white; }
        .btn-danger { background: #dc3545; color: white; }
        .btn-success { background: #28a745; color: white; }
        .btn-warning { background: #ffc107; color: black; }
        .btn-info { background: #17a2b8; color: white; }
        .input { padding: 8px; margin: 5px; border: 1px solid #ddd; border-radius: 4px; width: 80px; }
        .input-name { width: 120px; }
        .logs { background: #f8f9fa; padding: 10px; margin: 10px 0; max-height: 200px; overflow-y: auto; font-family: monospace; font-size: 12px; border: 1px solid #dee2e6; border-radius: 4px; }
        .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🤖 MMO Admin Panel - Bot Control System</h1>
        
        <div id="status" class="status disconnected">
            Disconnected
        </div>
        
        <div class="grid">
            <div class="section bot-section">
                <h3>🤖 Bot Management</h3>
                
                <h4>Spawn Bot</h4>
                <input type="text" id="botName" class="input input-name" placeholder="Bot Name" value="Bot">
                <input type="number" id="botX" class="input" placeholder="X" value="0" step="0.1">
                <input type="number" id="botY" class="input" placeholder="Y" value="0" step="0.1">
                <input type="number" id="botZ" class="input" placeholder="Z" value="0" step="0.1">
                <select id="botBehavior" class="input">
                    <option value="idle">Idle</option>
                    <option value="random">Random Movement</option>
                    <option value="patrol">Patrol</option>
                </select>
                <button class="btn btn-success" onclick="spawnBot()">🤖 Spawn Bot</button>
                
                <h4>Bot Commands</h4>
                <button class="btn btn-info" onclick="randomizeAllBots()">🎲 Randomize All Bots</button>
                <button class="btn btn-warning" onclick="stopAllBots()">⏹️ Stop All Bots</button>
                <button class="btn btn-danger" onclick="removeAllBots()">🗑️ Remove All Bots</button>
                
                <h4>🚀 Benchmark / Stress Testing</h4>
                <div style="background: #fff3cd; padding: 10px; margin: 10px 0; border-radius: 4px; border: 1px solid #ffeaa7;">
                    <strong>⚠️ Warning:</strong> Benchmark mode spawns many bots for performance testing
                </div>
                <input type="number" id="benchmarkCount" class="input" placeholder="Bot Count" value="100" min="1" max="1000">
                <select id="benchmarkBehavior" class="input">
                    <option value="random">Random Movement</option>
                    <option value="idle">Idle</option>
                    <option value="patrol">Patrol</option>
                </select>
                <input type="number" id="benchmarkSpread" class="input" placeholder="Spread Radius" value="50" step="5">
                <br>
                <button class="btn btn-warning" onclick="spawnBenchmarkBots()">🚀 Spawn Benchmark Bots</button>
                <button class="btn btn-danger" onclick="clearBenchmarkBots()">🧹 Clear Benchmark Bots</button>
                
                <div id="benchmarkMetrics" style="background: #e7f3ff; padding: 10px; margin: 10px 0; border-radius: 4px; border: 1px solid #007bff;">
                    <strong>📊 Real-time Server Metrics:</strong>
                    <div style="color: #666; margin-top: 5px;">Metrics will update automatically...</div>
                </div>
            </div>
            
            <div class="section">
                <h3>👥 Player Management</h3>
                
                <h4>Spawn NPC</h4>
                <input type="text" id="npcName" class="input input-name" placeholder="NPC Name" value="NPC">
                <input type="number" id="npcX" class="input" placeholder="X" value="0" step="0.1">
                <input type="number" id="npcY" class="input" placeholder="Y" value="0" step="0.1">
                <input type="number" id="npcZ" class="input" placeholder="Z" value="0" step="0.1">
                <button class="btn btn-success" onclick="spawnNPC()">👤 Spawn NPC</button>
                
                <h4>Global Commands</h4>
                <button class="btn btn-primary" onclick="teleportAll()">📍 Teleport All to Origin</button>
                <button class="btn btn-danger" onclick="kickAll()">🚫 Kick All Players</button>
            </div>
        </div>
        
        <div class="section">
            <h3>Connected Entities</h3>
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
                updateStatus('Connected - Bot Control Active', true);
                addLog('Connected to admin WebSocket with bot control');
                ws.send(JSON.stringify({op: 'admin_join', name: 'BotAdmin'}));
            };
            
            ws.onclose = () => {
                updateStatus('Disconnected', false);
                addLog('Disconnected from admin WebSocket');
                setTimeout(connect, 3000);
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
                    
                    // Update benchmark metrics if available
                    if (msg.benchmarkMetrics) {
                        updateBenchmarkMetrics(msg.benchmarkMetrics);
                    }
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
                const isBot = player.name.includes('Bot') || player.isNpc;
                div.className = `player ${isBot ? 'bot' : ''}`;
                
                const entityType = isBot ? '🤖' : '👤';
                const controls = isBot ? getBotControls(player.id) : getPlayerControls(player.id);
                
                div.innerHTML = `
                    <span>${entityType} <strong>${player.name}</strong> (${player.id.substring(0,8)}...) - Position: (${player.x.toFixed(1)}, ${player.y.toFixed(1)}, ${player.z.toFixed(1)})</span>
                    <div>${controls}</div>
                `;
                list.appendChild(div);
            });
        }

        function getBotControls(botId) {
            return `
                <button class="btn btn-info" onclick="setBotBehavior('${botId}', 'random')">🎲 Random</button>
                <button class="btn btn-warning" onclick="setBotBehavior('${botId}', 'patrol')">🚶 Patrol</button>
                <button class="btn btn-primary" onclick="setBotBehavior('${botId}', 'idle')">⏹️ Stop</button>
                <button class="btn btn-primary" onclick="controlBot('${botId}')">🎮 Control</button>
                <button class="btn btn-danger" onclick="kickPlayer('${botId}')">🗑️ Remove</button>
            `;
        }

        function getPlayerControls(playerId) {
            return `
                <button class="btn btn-primary" onclick="teleportPlayer('${playerId}')">📍 Teleport</button>
                <button class="btn btn-danger" onclick="kickPlayer('${playerId}')">🚫 Kick</button>
            `;
        }

        // Bot Functions
        function spawnBot() {
            const name = document.getElementById('botName').value || 'Bot';
            const x = parseFloat(document.getElementById('botX').value) || 0;
            const y = parseFloat(document.getElementById('botY').value) || 0;
            const z = parseFloat(document.getElementById('botZ').value) || 0;
            const behavior = document.getElementById('botBehavior').value || 'idle';
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_spawn_bot',
                    name: name,
                    x: x, y: y, z: z,
                    behavior: behavior
                }));
                addLog(`🤖 Spawning Bot: ${name} at (${x}, ${y}, ${z}) with ${behavior} behavior`);
            }
        }

        function setBotBehavior(botId, behavior) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_bot_behavior',
                    botId: botId,
                    behavior: behavior
                }));
                addLog(`🤖 Setting bot ${botId.substring(0,8)} behavior to ${behavior}`);
            }
        }

        function controlBot(botId) {
            const x = prompt('Enter X coordinate:') || 0;
            const y = prompt('Enter Y coordinate:') || 0;
            const z = prompt('Enter Z coordinate:') || 0;
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_control_bot',
                    botId: botId,
                    x: parseFloat(x),
                    y: parseFloat(y),
                    z: parseFloat(z)
                }));
                addLog(`🎮 Controlling bot ${botId.substring(0,8)} to (${x}, ${y}, ${z})`);
            }
        }

        function randomizeAllBots() {
            Object.values(players).forEach(player => {
                if (player.name.includes('Bot') || player.isNpc) {
                    setBotBehavior(player.id, 'random');
                }
            });
        }

        function stopAllBots() {
            Object.values(players).forEach(player => {
                if (player.name.includes('Bot') || player.isNpc) {
                    setBotBehavior(player.id, 'idle');
                }
            });
        }

        function removeAllBots() {
            if (confirm('Remove all bots?')) {
                Object.values(players).forEach(player => {
                    if (player.name.includes('Bot') || player.isNpc) {
                        kickPlayer(player.id);
                    }
                });
            }
        }

        // Original Functions
        function spawnNPC() {
            const name = document.getElementById('npcName').value || 'NPC';
            const x = parseFloat(document.getElementById('npcX').value) || 0;
            const y = parseFloat(document.getElementById('npcY').value) || 0;
            const z = parseFloat(document.getElementById('npcZ').value) || 0;
            
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_spawn_npc',
                    name: name, x: x, y: y, z: z
                }));
                addLog(`👤 Spawning NPC: ${name} at (${x}, ${y}, ${z})`);
            }
        }

        function teleportPlayer(playerId) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_teleport',
                    playerId: playerId,
                    x: 0, y: 0, z: 0
                }));
                addLog(`📍 Teleporting ${playerId.substring(0,8)} to origin`);
            }
        }

        function kickPlayer(playerId) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_kick',
                    playerId: playerId
                }));
                addLog(`🗑️ Removing ${playerId.substring(0,8)}`);
            }
        }

        function teleportAll() {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({
                    op: 'admin_teleport_all',
                    x: 0, y: 0, z: 0
                }));
                addLog('📍 Teleporting all to origin');
            }
        }

        function kickAll() {
            if (confirm('Kick all players?')) {
                if (ws && ws.readyState === WebSocket.OPEN) {
                    ws.send(JSON.stringify({op: 'admin_kick_all'}));
                    addLog('🚫 Kicking all players');
                }
            }
        }

        // Benchmark Functions
        function spawnBenchmarkBots() {
            const count = parseInt(document.getElementById('benchmarkCount').value) || 100;
            const behavior = document.getElementById('benchmarkBehavior').value || 'random';
            const spreadRadius = parseFloat(document.getElementById('benchmarkSpread').value) || 50;
            
            if (count > 500 && !confirm(`Are you sure you want to spawn ${count} bots? This may impact performance.`)) {
                return;
            }
            
            // Spawn bots directly like simple clients
            for (let i = 0; i < count; i++) {
                setTimeout(() => {
                    spawnSimpleBot(`Bot${Date.now()}_${i}`, behavior, spreadRadius);
                }, i * 50); // Stagger spawning to avoid overwhelming
            }
            addLog(`🚀 Spawning ${count} benchmark bots with ${behavior} behavior (spread: ${spreadRadius})`);
        }

        // Store bot connections to manage them
        const botConnections = [];

        function spawnSimpleBot(name, behavior, spreadRadius) {
            // Create a new WebSocket connection for each bot
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const botWs = new WebSocket(`${protocol}//${window.location.host}/ws`);
            
            botWs.onopen = function() {
                // Spawn the bot
                const x = (Math.random() - 0.5) * spreadRadius * 2;
                const z = (Math.random() - 0.5) * spreadRadius * 2;
                
                botWs.send(JSON.stringify({
                    action: 'spawn_bot',
                    botId: name,
                    x: x,
                    z: z,
                    strategy: behavior
                }));
                
                // Start bot behavior
                if (behavior === 'random') {
                    startRandomMovement(botWs);
                } else if (behavior === 'patrol') {
                    startPatrolMovement(botWs);
                }
                
                // Store connection for cleanup
                botConnections.push(botWs);
            };
            
            botWs.onerror = function(error) {
                console.log(`Bot ${name} WebSocket error:`, error);
            };
            
            botWs.onclose = function() {
                // Remove from connections array
                const index = botConnections.indexOf(botWs);
                if (index > -1) {
                    botConnections.splice(index, 1);
                }
            };
        }

        function startRandomMovement(botWs) {
            const moveInterval = setInterval(() => {
                if (botWs.readyState === WebSocket.OPEN) {
                    const x = (Math.random() - 0.5) * 20; // Random movement in 20x20 area
                    const z = (Math.random() - 0.5) * 20;
                    
                    botWs.send(JSON.stringify({
                        action: 'move',
                        x: x,
                        z: z
                    }));
                } else {
                    clearInterval(moveInterval);
                }
            }, 1000 + Math.random() * 2000); // Move every 1-3 seconds
        }

        function startPatrolMovement(botWs) {
            let patrolIndex = 0;
            const patrolPoints = [
                {x: -5, z: -5},
                {x: 5, z: -5},
                {x: 5, z: 5},
                {x: -5, z: 5}
            ];
            
            const patrolInterval = setInterval(() => {
                if (botWs.readyState === WebSocket.OPEN) {
                    const point = patrolPoints[patrolIndex % patrolPoints.length];
                    patrolIndex++;
                    
                    botWs.send(JSON.stringify({
                        action: 'move',
                        x: point.x,
                        z: point.z
                    }));
                } else {
                    clearInterval(patrolInterval);
                }
            }, 3000); // Move every 3 seconds
        }

        function clearBenchmarkBots() {
            if (confirm('Remove all benchmark bots?')) {
                // Close all bot connections
                botConnections.forEach(ws => {
                    if (ws.readyState === WebSocket.OPEN) {
                        ws.close();
                    }
                });
                botConnections.length = 0; // Clear the array
                addLog('🧹 Clearing all benchmark bots');
            }
        }

        function updateBenchmarkMetrics(metrics) {
            const metricsContainer = document.getElementById('benchmarkMetrics');
            if (metricsContainer) {
                metricsContainer.innerHTML = `
                    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px;">
                        <div><strong>Total Entities:</strong> ${metrics.totalEntities}</div>
                        <div><strong>Real Players:</strong> ${metrics.realPlayers}</div>
                        <div><strong>Total Bots:</strong> ${metrics.totalBots}</div>
                        <div><strong>Active Bots:</strong> ${metrics.activeBots}</div>
                        <div><strong>Memory Usage:</strong> ${metrics.memoryUsageMB} MB</div>
                        <div><strong>Update Rate:</strong> ${metrics.updatesPerSecond} Hz</div>
                    </div>
                `;
            }
        }

        connect();
    </script>
</body>
</html>
""";
}

app.Run();
