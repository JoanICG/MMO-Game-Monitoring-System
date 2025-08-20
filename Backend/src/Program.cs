using Backend;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles(); // Para servir archivos estáticos

var udpServer = new UdpGameServer(8081); // UDP on port 8081

// Start UDP server
_ = Task.Run(() => udpServer.StartAsync());

app.MapGet("/health", () => Results.Ok(new { status = "ok", udp_port = 8081 }));

// Simple status page instead of admin panel
app.MapGet("/admin", async ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(GetStatusPage());
});

// Bot Management Admin Panel
app.MapGet("/admin/bots", async ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(GetBotManagementPage());
});

// API endpoint for bot management stats
app.MapGet("/api/bot-stats", () =>
{
    var botManager = BotManagementSystem.Instance;
    var stats = botManager.GetSystemStats();
    return Results.Ok(stats);
});

// Bot Management API Endpoints
app.MapPost("/api/bot-containers", (CreateContainerRequest request) =>
{
    Console.WriteLine($"[DEBUG] Creating container: {request.Name} with {request.MaxBots} max bots");
    var botManager = BotManagementSystem.Instance;
    var container = botManager.CreateContainer(request.Name, request.MaxBots);
    if (container != null)
    {
        Console.WriteLine($"[DEBUG] Container created successfully: {container.Id}");
        return Results.Ok(new { containerId = container.Id, message = $"Container '{request.Name}' created successfully" });
    }
    Console.WriteLine($"[DEBUG] Failed to create container");
    return Results.BadRequest(new { message = "Failed to create container" });
});

app.MapDelete("/api/bot-containers/{containerId:guid}", (Guid containerId) =>
{
    var botManager = BotManagementSystem.Instance;
    if (botManager.RemoveContainer(containerId))
    {
        return Results.Ok(new { message = "Container removed successfully" });
    }
    return Results.NotFound(new { message = "Container not found" });
});

app.MapPost("/api/bot-containers/{containerId:guid}/bots", (Guid containerId, SpawnBotsRequest request) =>
{
    Console.WriteLine($"[DEBUG] Spawning {request.Count} bots in container {containerId}");
    var botManager = BotManagementSystem.Instance;
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        Console.WriteLine($"[DEBUG] Container {containerId} not found");
        return Results.NotFound(new { message = "Container not found" });
    }

    int spawned = 0;
    for (int i = 0; i < request.Count; i++)
    {
        if (container.AddBot($"Bot_{DateTime.Now:HHmmss}_{i + 1}"))
        {
            spawned++;
        }
    }

    Console.WriteLine($"[DEBUG] Spawned {spawned} bots successfully");
    return Results.Ok(new { spawned, message = $"Spawned {spawned} bots" });
});

app.MapDelete("/api/bot-containers/{containerId:guid}/bots", (Guid containerId) =>
{
    var botManager = BotManagementSystem.Instance;
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    container.RemoveAllBots();
    return Results.Ok(new { message = "All bots removed from container" });
});

app.MapPost("/api/bot-containers/{containerId:guid}/pause", (Guid containerId) =>
{
    var botManager = BotManagementSystem.Instance;
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    container.PauseAllBots();
    return Results.Ok(new { message = "Container bots paused" });
});

app.MapPost("/api/bot-containers/{containerId:guid}/resume", (Guid containerId) =>
{
    var botManager = BotManagementSystem.Instance;
    var container = botManager.GetContainer(containerId);
    if (container == null)
    {
        return Results.NotFound(new { message = "Container not found" });
    }

    container.ResumeAllBots();
    return Results.Ok(new { message = "Container bots resumed" });
});

app.MapPost("/api/bots/pause-all", () =>
{
    var botManager = BotManagementSystem.Instance;
    botManager.PauseAllBots();
    return Results.Ok(new { message = "All bots paused" });
});

app.MapPost("/api/bots/resume-all", () =>
{
    var botManager = BotManagementSystem.Instance;
    botManager.ResumeAllBots();
    return Results.Ok(new { message = "All bots resumed" });
});

app.MapDelete("/api/bots/remove-all", () =>
{
    var botManager = BotManagementSystem.Instance;
    botManager.RemoveAllBots();
    return Results.Ok(new { message = "All bots removed" });
});

app.MapDelete("/api/bot-containers", () =>
{
    var botManager = BotManagementSystem.Instance;
    botManager.RemoveAllContainers();
    return Results.Ok(new { message = "All containers removed" });
});

static string GetStatusPage()
{
    return """
<!DOCTYPE html>
<html>
<head>
    <title>🚀 MMO Server Status - UDP Only</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; text-align: center; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 40px; border-radius: 8px; }
        .status { padding: 20px; margin: 20px 0; border-radius: 8px; background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }
        .info { background: #e7f3ff; padding: 20px; margin: 20px 0; border-radius: 8px; border: 1px solid #007bff; }
        .code { background: #f8f9fa; padding: 15px; border-radius: 4px; font-family: monospace; margin: 10px 0; border: 1px solid #dee2e6; }
        h1 { color: #333; margin-bottom: 30px; }
        h2 { color: #666; margin-top: 30px; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🚀 MMO Server - UDP Protocol</h1>
        
        <div class="status">
            ✅ Server is running on UDP port 8081
        </div>
        
        <div class="info">
            <h2>📡 Connection Information</h2>
            <p><strong>Protocol:</strong> UDP (High Performance)</p>
            <p><strong>Port:</strong> 8081</p>
            <p><strong>Server IP:</strong> localhost (127.0.0.1)</p>
        </div>
        
        <div class="info">
            <h2>🎮 Unity Client Setup</h2>
            <p>Make sure your Unity client is configured to connect via UDP:</p>
            <div class="code">
                Server Host: localhost<br>
                Server Port: 8081<br>
                Protocol: UDP
            </div>
        </div>
        
        <div class="info">
            <h2>🔧 Technical Details</h2>
            <p><strong>Why UDP?</strong></p>
            <ul style="text-align: left; display: inline-block;">
                <li>⚡ Lower latency than WebSocket/TCP</li>
                <li>🚀 Better performance for real-time games</li>
                <li>📦 Smaller packet overhead</li>
                <li>🎯 Designed for fast-paced multiplayer gaming</li>
            </ul>
        </div>
        
        <div class="info">
            <h2>📊 Server Capabilities</h2>
            <ul style="text-align: left; display: inline-block;">
                <li>✅ Player join/leave management</li>
                <li>✅ Real-time movement synchronization</li>
                <li>✅ Client-side prediction support</li>
                <li>✅ Server authority with reconciliation</li>
                <li>✅ Advanced bot management system</li>
                <li>✅ Performance optimization for 100+ entities</li>
            </ul>
        </div>
        
        <div class="info">
            <h2>🤖 Bot Management</h2>
            <p>El servidor incluye un sistema avanzado de gestión de bots con:</p>
            <ul style="text-align: left; display: inline-block;">
                <li>🏗️ Contenedores de bots para organización eficiente</li>
                <li>🎯 Lógica de movimiento aleatorio inteligente</li>
                <li>⚡ Control de rendimiento y optimización</li>
                <li>📊 Estadísticas en tiempo real</li>
                <li>🎮 Interfaz de administración web</li>
            </ul>
            <p style="margin-top: 20px;">
                <a href="/admin/bots" style="background: #3498db; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; display: inline-block;">
                    🤖 Abrir Panel de Gestión de Bots
                </a>
            </p>
        </div>
        
        <p style="margin-top: 40px; color: #666; font-size: 14px;">
            WebSocket functionality has been removed for optimal UDP performance
        </p>
    </div>
</body>
</html>
""";
}

static string GetBotManagementPage()
{
    return """
<!DOCTYPE html>
<html>
<head>
    <title>🤖 Bot Management System - MMO Server</title>
    <style>
        body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: #f5f5f5; }
        .header { background: #2c3e50; color: white; padding: 20px; text-align: center; }
        .container { max-width: 1200px; margin: 20px auto; padding: 20px; }
        .card { background: white; margin: 20px 0; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }
        .stat-card { background: #3498db; color: white; padding: 20px; border-radius: 8px; text-align: center; }
        .stat-number { font-size: 2em; font-weight: bold; }
        .stat-label { margin-top: 5px; opacity: 0.9; }
        .btn { padding: 10px 20px; margin: 5px; border: none; border-radius: 4px; cursor: pointer; font-size: 14px; }
        .btn-primary { background: #3498db; color: white; }
        .btn-success { background: #27ae60; color: white; }
        .btn-warning { background: #f39c12; color: white; }
        .btn-danger { background: #e74c3c; color: white; }
        .btn:hover { opacity: 0.9; }
        .form-group { margin: 15px 0; }
        .form-group label { display: block; margin-bottom: 5px; font-weight: bold; }
        .form-group input, .form-group select { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; }
        .container-list { margin-top: 20px; }
        .container-item { border: 1px solid #ddd; margin: 10px 0; padding: 15px; border-radius: 8px; background: #f9f9f9; }
        .container-header { display: flex; justify-content: between; align-items: center; margin-bottom: 10px; }
        .container-name { font-size: 1.2em; font-weight: bold; color: #2c3e50; }
        .container-stats { font-size: 0.9em; color: #666; margin: 5px 0; }
        .bot-actions { margin-top: 10px; }
        .log { background: #2c3e50; color: #00ff00; padding: 15px; border-radius: 8px; font-family: 'Courier New', monospace; height: 200px; overflow-y: auto; margin-top: 20px; }
        .status-indicator { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 8px; }
        .status-active { background: #27ae60; }
        .status-inactive { background: #e74c3c; }
    </style>
</head>
<body>
    <div class="header">
        <h1>🤖 Bot Management System</h1>
        <p>Control y gestión avanzada de bots para MMO Server</p>
    </div>
    
    <div class="container">
        <!-- System Stats -->
        <div class="card">
            <h2>📊 Estadísticas del Sistema</h2>
            <div class="stats-grid" id="statsGrid">
                <div class="stat-card">
                    <div class="stat-number" id="totalContainers">0</div>
                    <div class="stat-label">Contenedores</div>
                </div>
                <div class="stat-card">
                    <div class="stat-number" id="totalBots">0</div>
                    <div class="stat-label">Total Bots</div>
                </div>
                <div class="stat-card">
                    <div class="stat-number" id="activeBots">0</div>
                    <div class="stat-label">Bots Activos</div>
                </div>
                <div class="stat-card">
                    <div class="stat-number" id="pausedBots">0</div>
                    <div class="stat-label">Bots Pausados</div>
                </div>
            </div>
            <button class="btn btn-primary" onclick="refreshStats()">🔄 Actualizar Stats</button>
        </div>
        
        <!-- Container Management -->
        <div class="card">
            <h2>📦 Gestión de Contenedores</h2>
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
                <div>
                    <h3>Crear Nuevo Contenedor</h3>
                    <div class="form-group">
                        <label>Nombre del Contenedor:</label>
                        <input type="text" id="containerName" placeholder="Ej: BotGroup_01" value="BotGroup_01">
                    </div>
                    <div class="form-group">
                        <label>Máximo de Bots:</label>
                        <input type="number" id="maxBots" min="1" max="100" value="25">
                    </div>
                    <button class="btn btn-success" onclick="createContainer()">➕ Crear Contenedor</button>
                </div>
                <div>
                    <h3>Acciones Globales</h3>
                    <button class="btn btn-warning" onclick="pauseAllBots()">⏸️ Pausar Todos los Bots</button>
                    <button class="btn btn-success" onclick="resumeAllBots()">▶️ Resumir Todos los Bots</button>
                    <button class="btn btn-danger" onclick="removeAllBots()">🗑️ Eliminar Todos los Bots</button>
                    <button class="btn btn-danger" onclick="removeAllContainers()">🗑️ Eliminar Todos los Contenedores</button>
                </div>
            </div>
        </div>
        
        <!-- Container List -->
        <div class="card">
            <h2>🗂️ Lista de Contenedores</h2>
            <div id="containerList" class="container-list">
                <p>Cargando contenedores...</p>
            </div>
        </div>
        
        <!-- Quick Actions -->
        <div class="card">
            <h2>⚡ Acciones Rápidas</h2>
            <button class="btn btn-primary" onclick="createTestEnvironment()">🧪 Crear Entorno de Prueba</button>
            <button class="btn btn-warning" onclick="createStressTest()">🔥 Test de Estrés (50 bots)</button>
            <button class="btn btn-success" onclick="createDefaultContainer()">📦 Contenedor por Defecto</button>
        </div>
        
        <!-- System Log -->
        <div class="card">
            <h2>📋 Log del Sistema</h2>
            <div id="systemLog" class="log">
                [BOT MANAGEMENT] Sistema iniciado...<br>
                [INFO] Esperando comandos...<br>
            </div>
        </div>
    </div>

    <script>
        let currentStats = {};
        
        // Auto-refresh stats every 5 seconds
        setInterval(refreshStats, 5000);
        
        // Initial load
        refreshStats();
        
        function refreshStats() {
            fetch('/api/bot-stats')
                .then(response => response.json())
                .then(data => {
                    currentStats = data;
                    updateStatsDisplay(data);
                    updateContainerList(data.containerStats);
                })
                .catch(error => {
                    logMessage(`[ERROR] Failed to fetch stats: ${error.message}`, 'error');
                });
        }
        
        function updateStatsDisplay(stats) {
            document.getElementById('totalContainers').textContent = stats.totalContainers;
            document.getElementById('totalBots').textContent = stats.totalBots;
            document.getElementById('activeBots').textContent = stats.activeBots;
            document.getElementById('pausedBots').textContent = stats.pausedBots;
        }
        
        function updateContainerList(containers) {
            const containerList = document.getElementById('containerList');
            
            if (!containers || containers.length === 0) {
                containerList.innerHTML = '<p>No hay contenedores activos.</p>';
                return;
            }
            
            containerList.innerHTML = containers.map(container => `
                <div class="container-item">
                    <div class="container-header">
                        <div>
                            <span class="status-indicator ${container.isActive ? 'status-active' : 'status-inactive'}"></span>
                            <span class="container-name">${container.containerName}</span>
                        </div>
                        <div>
                            <button class="btn btn-danger" onclick="removeContainer('${container.containerId}')">🗑️ Eliminar</button>
                        </div>
                    </div>
                    <div class="container-stats">
                        📊 Total: ${container.totalBots}/${container.maxBots} | 
                        ✅ Activos: ${container.activeBots} | 
                        ⏸️ Pausados: ${container.pausedBots} |
                        📅 Creado: ${new Date(container.createdAt).toLocaleString()}
                    </div>
                    <div class="bot-actions">
                        <button class="btn btn-success" onclick="spawnBots('${container.containerId}', 5)">➕ Agregar 5 Bots</button>
                        <button class="btn btn-success" onclick="spawnBots('${container.containerId}', 10)">➕ Agregar 10 Bots</button>
                        <button class="btn btn-warning" onclick="pauseContainerBots('${container.containerId}')">⏸️ Pausar</button>
                        <button class="btn btn-success" onclick="resumeContainerBots('${container.containerId}')">▶️ Resumir</button>
                        <button class="btn btn-danger" onclick="removeContainerBots('${container.containerId}')">🗑️ Eliminar Bots</button>
                    </div>
                </div>
            `).join('');
        }
        
        function logMessage(message, type = 'info') {
            const log = document.getElementById('systemLog');
            const timestamp = new Date().toLocaleTimeString();
            const color = type === 'error' ? '#ff6b6b' : type === 'success' ? '#51cf66' : '#00ff00';
            log.innerHTML += `<span style="color: ${color}">[${timestamp}] ${message}</span><br>`;
            log.scrollTop = log.scrollHeight;
        }
        
        // API Functions 
        function createContainer() {
            const name = document.getElementById('containerName').value;
            const maxBots = parseInt(document.getElementById('maxBots').value);
            
            if (!name.trim()) {
                logMessage('[ERROR] El nombre del contenedor no puede estar vacío', 'error');
                return;
            }
            
            logMessage(`[ADMIN] Creando contenedor '${name}' con máximo ${maxBots} bots...`);
            
            fetch('/api/bot-containers', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ name: name, maxBots: maxBots })
            })
            .then(response => response.json())
            .then(data => {
                if (data.message) {
                    logMessage(`[SUCCESS] ${data.message}`, 'success');
                    refreshStats();
                } else {
                    logMessage('[ERROR] Error al crear contenedor', 'error');
                }
            })
            .catch(error => {
                logMessage(`[ERROR] Error al crear contenedor: ${error.message}`, 'error');
            });
        }
        
        function removeContainer(containerId) {
            if (!confirm('¿Estás seguro de que quieres eliminar este contenedor y todos sus bots?')) {
                return;
            }
            
            logMessage(`[ADMIN] Eliminando contenedor ${containerId}...`);
            
            fetch(`/api/bot-containers/${containerId}`, {
                method: 'DELETE'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al eliminar contenedor: ${error.message}`, 'error');
            });
        }
        
        function spawnBots(containerId, count) {
            logMessage(`[ADMIN] Creando ${count} bots en contenedor ${containerId}...`);
            
            fetch(`/api/bot-containers/${containerId}/bots`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ count: count })
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al crear bots: ${error.message}`, 'error');
            });
        }
        
        function pauseContainerBots(containerId) {
            logMessage(`[ADMIN] Pausando bots en contenedor ${containerId}...`);
            
            fetch(`/api/bot-containers/${containerId}/pause`, {
                method: 'POST'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al pausar bots: ${error.message}`, 'error');
            });
        }
        
        function resumeContainerBots(containerId) {
            logMessage(`[ADMIN] Resumiendo bots en contenedor ${containerId}...`);
            
            fetch(`/api/bot-containers/${containerId}/resume`, {
                method: 'POST'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al resumir bots: ${error.message}`, 'error');
            });
        }
        
        function removeContainerBots(containerId) {
            if (!confirm('¿Estás seguro de que quieres eliminar todos los bots de este contenedor?')) {
                return;
            }
            
            logMessage(`[ADMIN] Eliminando todos los bots del contenedor ${containerId}...`);
            
            fetch(`/api/bot-containers/${containerId}/bots`, {
                method: 'DELETE'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al eliminar bots: ${error.message}`, 'error');
            });
        }
        
        function pauseAllBots() {
            logMessage(`[ADMIN] Pausando todos los bots del sistema...`);
            
            fetch('/api/bots/pause-all', {
                method: 'POST'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al pausar bots: ${error.message}`, 'error');
            });
        }
        
        function resumeAllBots() {
            logMessage(`[ADMIN] Resumiendo todos los bots del sistema...`);
            
            fetch('/api/bots/resume-all', {
                method: 'POST'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al resumir bots: ${error.message}`, 'error');
            });
        }
        
        function removeAllBots() {
            if (!confirm('¿Estás seguro de que quieres eliminar TODOS los bots del sistema?')) {
                return;
            }
            
            logMessage(`[ADMIN] Eliminando todos los bots del sistema...`);
            
            fetch('/api/bots/remove-all', {
                method: 'DELETE'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al eliminar bots: ${error.message}`, 'error');
            });
        }
        
        function removeAllContainers() {
            if (!confirm('¿Estás seguro de que quieres eliminar TODOS los contenedores y bots?')) {
                return;
            }
            
            logMessage(`[ADMIN] Eliminando todos los contenedores...`);
            
            fetch('/api/bot-containers', {
                method: 'DELETE'
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al eliminar contenedores: ${error.message}`, 'error');
            });
        }
        
        function createTestEnvironment() {
            logMessage(`[ADMIN] Creando entorno de prueba...`);
            
            // Crear dos contenedores de prueba
            Promise.all([
                fetch('/api/bot-containers', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: 'TestContainer_1', maxBots: 20 })
                }),
                fetch('/api/bot-containers', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: 'TestContainer_2', maxBots: 30 })
                })
            ])
            .then(() => {
                logMessage(`[SUCCESS] Entorno de prueba creado con 2 contenedores`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al crear entorno de prueba: ${error.message}`, 'error');
            });
        }
        
        function createStressTest() {
            logMessage(`[ADMIN] Iniciando test de estrés con 50 bots...`);
            
            // Crear contenedor de stress test
            fetch('/api/bot-containers', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: 'StressTest_Container', maxBots: 50 })
            })
            .then(response => response.json())
            .then(data => {
                if (data.containerId) {
                    // Agregar 50 bots al contenedor
                    return fetch(`/api/bot-containers/${data.containerId}/bots`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ count: 50 })
                    });
                }
            })
            .then(() => {
                logMessage(`[SUCCESS] Test de estrés iniciado con 50 bots`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al crear test de estrés: ${error.message}`, 'error');
            });
        }
        
        function createDefaultContainer() {
            logMessage(`[ADMIN] Creando contenedor por defecto...`);
            
            fetch('/api/bot-containers', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: 'DefaultContainer', maxBots: 25 })
            })
            .then(response => response.json())
            .then(data => {
                logMessage(`[SUCCESS] ${data.message}`, 'success');
                refreshStats();
            })
            .catch(error => {
                logMessage(`[ERROR] Error al crear contenedor por defecto: ${error.message}`, 'error');
            });
        }
    </script>
</body>
</html>
""";
}

app.Run();

// Request models for API endpoints
public record CreateContainerRequest(string Name, int MaxBots = 50);
public record SpawnBotsRequest(int Count);
