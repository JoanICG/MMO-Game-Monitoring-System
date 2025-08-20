using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Serializable message classes for JSON
[Serializable]
public class JoinMessage
{
    public string op = "join";
    public string name;
}

[Serializable]
public class MoveMessage
{
    public string op = "move";
    public float x, y, z;
    public float speed;
    public float time;
}

[Serializable]
public class HeartbeatMessage
{
    public string op = "heartbeat";
    public float time;
}

public class UdpNetworkClient : MonoBehaviour
{
    public static UdpNetworkClient Instance;

    [Header("Connection")]
    public string serverHost = "192.168.0.110";  // Server machine IP for network play
    public int serverPort = 8081;
    public string playerName = "Player";
    [Tooltip("If checked, will try localhost first, then fall back to serverHost")]
    public bool tryLocalFirst = true;

    [Header("Network Settings")]
    public int sendRate = 20; // Hz
    public int maxPacketSize = 1024;
    public float heartbeatInterval = 5f;

    [Header("Debug")]
    public bool autoJoin = true;
    public Guid localPlayerId;

    private UdpClient _udpClient;
    private IPEndPoint _serverEndpoint;
    private CancellationTokenSource _cts;
    private bool _connected = false;
    private float _lastHeartbeat;

    // Player management
    public readonly Dictionary<Guid, RemotePlayer> Players = new();

    [Serializable]
    public class RemotePlayer
    {
        public Guid id;
        public string name;
        public Vector3 pos;
        public Vector3 targetPos;
        public GameObject go;
        public float lastUpdateTime;
    }

    private async void Awake()
    {
        Debug.Log("[UdpNetworkClient] Awake called - Starting UDP client");
        
        if (Instance != null && Instance != this)
        {
            Debug.Log("[UdpNetworkClient] Instance already exists, destroying duplicate");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialize object pool if using object pooling
        /* Commented out for now - will implement later
        if (useObjectPooling)
        {
            // Create object pool if it doesn't exist
            if (GameObjectPool.Instance == null)
            {
                var poolObj = new GameObject("GameObjectPool");
                DontDestroyOnLoad(poolObj);
                objectPool = poolObj.AddComponent<GameObjectPool>();
            }
            else
            {
                objectPool = GameObjectPool.Instance;
            }
        }
        */

        Debug.Log($"[UdpNetworkClient] Instance set, autoJoin={autoJoin}");
        
        if (autoJoin)
        {
            Debug.Log("[UdpNetworkClient] Auto-joining server...");
            await ConnectAndJoin();
        }
    }

    public async Task ConnectAndJoin()
    {
        try
        {
            Debug.Log($"[UdpNetworkClient] Starting connection to {serverHost}:{serverPort}");
            _serverEndpoint = new IPEndPoint(IPAddress.Parse(GetServerIP()), serverPort);
            _udpClient = new UdpClient();
            _cts = new CancellationTokenSource();

            Debug.Log($"[UdpNetworkClient] Connecting to {_serverEndpoint}");
            
            // Start receive loop
            _ = ReceiveLoop();
            
            // Send join message
            await SendJoin();
            _connected = true;
            _lastHeartbeat = Time.time;
            
            Debug.Log("[UdpNetworkClient] Connected and join sent");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UdpNetworkClient] Connection error: {ex.Message}");
        }
    }

    private string GetServerIP()
    {
        // If tryLocalFirst is enabled and we're in editor/development, try localhost first
        if (tryLocalFirst && Application.isEditor)
        {
            Debug.Log("[UdpNetworkClient] Editor mode: trying localhost first");
            return "127.0.0.1";
        }
        
        // Convert localhost to IP for UDP, otherwise use configured host
        if (serverHost == "localhost") return "127.0.0.1";
        return serverHost;
    }

    private async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                var data = result.Buffer;
                var json = Encoding.UTF8.GetString(data);
                
                Debug.Log($"[UdpNetworkClient] RX: {json}");
                HandleMessage(json);
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Debug.LogWarning($"[UdpNetworkClient] Receive error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    public async Task SendPlayerInput(uint sequence, Vector2 input, float speed)
    {
        if (!_connected) return;

        var message = new
        {
            op = "input",
            seq = sequence,
            x = input.x,
            y = input.y,
            speed = speed,
            time = Time.time
        };

        await SendMessage(message);
    }

    private async Task SendJoin()
    {
        var message = new JoinMessage { name = playerName };
        await SendMessage(message);
    }

    private async Task SendHeartbeat()
    {
        var message = new HeartbeatMessage { time = Time.time };
        await SendMessage(message);
    }

    private async Task SendMessage(object message)
    {
        try
        {
            var json = JsonUtility.ToJson(message);
            Debug.Log($"[UdpNetworkClient] Sending JSON: {json}");
            var data = Encoding.UTF8.GetBytes(json);
            Debug.Log($"[UdpNetworkClient] Sending {data.Length} bytes to {_serverEndpoint}");
            
            if (data.Length > maxPacketSize)
            {
                Debug.LogWarning($"[UdpNetworkClient] Packet too large: {data.Length} bytes");
                return;
            }

            await _udpClient.SendAsync(data, data.Length, _serverEndpoint);
            Debug.Log("[UdpNetworkClient] Message sent successfully");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UdpNetworkClient] Send error: {ex.Message}");
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            if (json.Contains("\"op\":\"join_ack\""))
            {
                var startIndex = json.IndexOf("\"id\":\"") + 6;
                var endIndex = json.IndexOf("\"", startIndex);
                var idStr = json.Substring(startIndex, endIndex - startIndex);
                
                if (Guid.TryParse(idStr, out var gid))
                {
                    localPlayerId = gid;
                    Debug.Log($"[UdpNetworkClient] Assigned local id {localPlayerId}");
                }
            }
            else if (json.Contains("\"op\":\"state\""))
            {
                HandleStateMessage(json);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UdpNetworkClient] Message handling error: {ex.Message}");
        }
    }

    private void HandleStateMessage(string json)
    {
        try
        {
            // Remove excessive debug logging for performance
            // Debug.Log($"[UdpNetworkClient] Full JSON received: {json}");
            
            // Simple JSON parsing for state updates
            var playersIndex = json.IndexOf("\"players\":[");
            if (playersIndex < 0) 
            {
                Debug.LogWarning("[UdpNetworkClient] No 'players' array found in JSON");
                return;
            }

            var arrayPart = json.Substring(playersIndex + 11);
            arrayPart = arrayPart.TrimEnd('}', '\n', '\r');
            
            // Only log occasionally for debugging
            // Debug.Log($"[UdpNetworkClient] Players array part: {arrayPart}");

            var keep = new HashSet<Guid>();
            var playerChunks = arrayPart.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
            
            // Log player count every 60 frames to monitor performance
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[UdpNetworkClient] Processing {playerChunks.Length} players at frame {Time.frameCount}");
            }

            for (int i = 0; i < playerChunks.Length; i++)
            {
                var chunk = playerChunks[i];
                
                // Only log first few chunks for debugging
                if (i < 2 && Time.frameCount % 60 == 0) 
                {
                    Debug.Log($"[UdpNetworkClient] Processing chunk: {chunk}");
                }
                
                var id = ExtractGuid(chunk, "\"id\":\"");
                if (id == Guid.Empty) 
                {
                    if (i < 2) Debug.LogWarning($"[UdpNetworkClient] Could not extract ID from chunk: {chunk}");
                    continue;
                }

                var name = ExtractString(chunk, "\"name\":\"");
                var x = ExtractFloat(chunk, "\"x\":");
                var y = ExtractFloat(chunk, "\"y\":");
                var z = ExtractFloat(chunk, "\"z\":");
                var isNPC = ExtractBool(chunk, "\"isNPC\":");

                // Only log coordinates for debugging occasionally
                if (i < 2 && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[UdpNetworkClient] Extracted - ID: {id}, Name: {name}, Pos: ({x},{y},{z}), IsNPC: {isNPC}");
                }

                keep.Add(id);
                UpdateOrCreatePlayer(id, name, new Vector3(x, y, z), isNPC);
            }

            // Remove disconnected players
            var toRemove = new List<Guid>();
            foreach (var kv in Players)
            {
                if (!keep.Contains(kv.Key))
                {
                    if (kv.Value.go) Destroy(kv.Value.go);
                    toRemove.Add(kv.Key);
                }
            }
            foreach (var id in toRemove) Players.Remove(id);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UdpNetworkClient] State parsing error: {ex.Message}");
        }
    }

    private void UpdateOrCreatePlayer(Guid id, string name, Vector3 position, bool isNPC = false)
    {
        // Remove excessive logging for performance
        // Debug.Log($"[UdpNetworkClient] UpdateOrCreatePlayer called - ID: {id}, Name: {name}, Position: {position}, IsNPC: {isNPC}, LocalID: {localPlayerId}");
        
        if (!Players.TryGetValue(id, out var player))
        {
            // Create new player - ONLY create GameObject once
            player = new RemotePlayer
            {
                id = id,
                name = name,
                pos = position,
                targetPos = position
            };

            // Create GameObject only once and keep it
            player.go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.go.name = id == localPlayerId ? $"Local_{name}" : 
                             isNPC ? $"Bot_{name}" : $"Remote_{name}";
            player.go.transform.position = position;

            // Set up renderer once
            var renderer = player.go.GetComponent<Renderer>();
            if (renderer)
            {
                if (id == localPlayerId)
                {
                    renderer.material.color = Color.green;
                    // Add local player controller only once
                    if (player.go.GetComponent<LocalPlayerController>() == null)
                    {
                        var controller = player.go.AddComponent<LocalPlayerController>();
                        controller.SetNetworkClient(this);
                        Debug.Log("[UdpNetworkClient] Added LocalPlayerController to local player");
                    }
                }
                else if (isNPC)
                {
                    renderer.material.color = Color.red;
                    player.go.transform.localScale = Vector3.one * 1.5f;
                }
                else
                {
                    renderer.material.color = Color.blue;
                }
            }

            Players[id] = player;
            
            // Only log creation occasionally
            if (id == localPlayerId || Time.frameCount % 300 == 0)
            {
                Debug.Log($"[UdpNetworkClient] Created {(id == localPlayerId ? "LOCAL" : (isNPC ? "BOT" : "REMOTE"))} player {name}");
            }
        }
        else
        {
            // Player exists - just update position (NO GameObject creation/destruction)
            player.targetPos = position;
            
            // Update name if it changed (rare)
            if (player.name != name)
            {
                player.name = name;
                if (player.go != null)
                {
                    player.go.name = id == localPlayerId ? $"Local_{name}" : 
                                     isNPC ? $"Bot_{name}" : $"Remote_{name}";
                }
            }
        }

        // Handle local player reconciliation
        if (player.id == localPlayerId)
        {
            var controller = player.go?.GetComponent<LocalPlayerController>();
            if (controller != null)
            {
                controller.ReceiveServerState(position, Time.time);
            }
        }

        player.lastUpdateTime = Time.time;
        
        // Debug: Count total objects in scene (only occasionally for performance)
        if (Time.frameCount % 300 == 0) // Every 5 seconds at 60fps
        {
            int totalPlayers = Players.Count;
            int activeBots = 0;
            foreach (var p in Players.Values)
            {
                if (p.go != null && p.go.name.StartsWith("Bot_"))
                    activeBots++;
            }
            Debug.Log($"[UdpNetworkClient] Total players in dictionary: {totalPlayers}, Active bot GameObjects: {activeBots}");
        }
    }

    [Header("Performance Settings")]
    [SerializeField] private bool useBinaryProtocol = false; // Disabled for now
    [SerializeField] private bool useObjectPooling = false; // Disabled for now
    
    // private GameObjectPool objectPool; // Commented out for now
    private int _updateIndex = 0; // For batched updates

    private void Update()
    {
        // Send heartbeat
        if (_connected && Time.time - _lastHeartbeat >= heartbeatInterval)
        {
            _ = SendHeartbeat();
            _lastHeartbeat = Time.time;
        }

        // Interpolate remote players in batches to improve performance
        var playerList = Players.Values.ToList();
        const int playersPerFrame = 20; // Process only 20 players per frame
        
        for (int i = 0; i < playersPerFrame && i < playerList.Count; i++)
        {
            var index = (_updateIndex + i) % playerList.Count;
            var player = playerList[index];
            
            if (player.go == null || player.id == localPlayerId) continue;

            var current = player.go.transform.position;
            var target = player.targetPos;
            
            // Use faster interpolation for smoother movement
            player.go.transform.position = Vector3.Lerp(current, target, 15f * Time.deltaTime);
        }
        
        _updateIndex = (_updateIndex + playersPerFrame) % Math.Max(playerList.Count, 1);
    }

    // Helper methods for JSON parsing
    private Guid ExtractGuid(string src, string marker)
    {
        var startIndex = src.IndexOf(marker);
        if (startIndex < 0) return Guid.Empty;
        startIndex += marker.Length;
        var endIndex = src.IndexOf("\"", startIndex);
        if (endIndex < 0) return Guid.Empty;
        var guidStr = src.Substring(startIndex, endIndex - startIndex);
        return Guid.TryParse(guidStr, out var guid) ? guid : Guid.Empty;
    }

    private string ExtractString(string src, string marker)
    {
        var startIndex = src.IndexOf(marker);
        if (startIndex < 0) return string.Empty;
        startIndex += marker.Length;
        var endIndex = src.IndexOf("\"", startIndex);
        if (endIndex < 0) return string.Empty;
        return src.Substring(startIndex, endIndex - startIndex);
    }

    private float ExtractFloat(string src, string marker)
    {
        var startIndex = src.IndexOf(marker);
        if (startIndex < 0) return 0f;
        startIndex += marker.Length;
        var endIndex = startIndex;
        
        // Extract only valid float characters: digits, minus sign, and decimal point
        while (endIndex < src.Length)
        {
            char c = src[endIndex];
            if (char.IsDigit(c) || c == '.' || c == '-')
                endIndex++;
            else
                break; // Stop at comma, space, or any other delimiter
        }
        
        var floatStr = src.Substring(startIndex, endIndex - startIndex);
        
        // Remove debug log to improve performance - only log parsing errors
        // Debug.Log($"[UdpNetworkClient] ExtractFloat: marker='{marker}', extracted='{floatStr}'");
        
        return float.TryParse(floatStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;
    }

    private bool ExtractBool(string src, string marker)
    {
        var startIndex = src.IndexOf(marker);
        if (startIndex < 0) return false;
        startIndex += marker.Length;
        
        if (startIndex + 4 <= src.Length && src.Substring(startIndex, 4) == "true")
            return true;
        if (startIndex + 5 <= src.Length && src.Substring(startIndex, 5) == "false")
            return false;
            
        return false;
    }

    private void OnApplicationQuit()
    {
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
        }
        catch { }
    }
}
