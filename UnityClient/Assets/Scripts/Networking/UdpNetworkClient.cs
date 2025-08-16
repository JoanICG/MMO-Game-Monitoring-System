using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class UdpNetworkClient : MonoBehaviour
{
    public static UdpNetworkClient Instance;

    [Header("Connection")]
    public string serverHost = "localhost";
    public int serverPort = 8081;
    public string playerName = "Player";

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
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (autoJoin)
        {
            await ConnectAndJoin();
        }
    }

    public async Task ConnectAndJoin()
    {
        try
        {
            _serverEndpoint = new IPEndPoint(IPAddress.Parse(GetServerIP()), serverPort);
            _udpClient = new UdpClient();
            _cts = new CancellationTokenSource();

            Debug.Log($"[UdpNetworkClient] Connecting to {_serverEndpoint}");
            
            // Start receive loop
            _ = ReceiveLoop();
            
            // Send join message
            await SendJoin();
            _connected = true;
            
            Debug.Log("[UdpNetworkClient] Connected and join sent");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UdpNetworkClient] Connection error: {ex.Message}");
        }
    }

    private string GetServerIP()
    {
        // Convert localhost to IP for UDP
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
        var message = new { op = "join", name = playerName };
        await SendMessage(message);
    }

    private async Task SendHeartbeat()
    {
        var message = new { op = "heartbeat", time = Time.time };
        await SendMessage(message);
    }

    private async Task SendMessage(object message)
    {
        try
        {
            var json = JsonUtility.ToJson(message);
            var data = Encoding.UTF8.GetBytes(json);
            
            if (data.Length > maxPacketSize)
            {
                Debug.LogWarning($"[UdpNetworkClient] Packet too large: {data.Length} bytes");
                return;
            }

            await _udpClient.SendAsync(data, data.Length, _serverEndpoint);
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
            // Simple JSON parsing for state updates
            var playersIndex = json.IndexOf("\"players\":[");
            if (playersIndex < 0) return;

            var arrayPart = json.Substring(playersIndex + 11);
            arrayPart = arrayPart.TrimEnd('}', '\n', '\r');

            var keep = new HashSet<Guid>();
            var playerChunks = arrayPart.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var chunk in playerChunks)
            {
                var id = ExtractGuid(chunk, "\"id\":\"");
                if (id == Guid.Empty) continue;

                var name = ExtractString(chunk, "\"name\":\"");
                var x = ExtractFloat(chunk, "\"x\":");
                var y = ExtractFloat(chunk, "\"y\":");
                var z = ExtractFloat(chunk, "\"z\":");

                keep.Add(id);
                UpdateOrCreatePlayer(id, name, new Vector3(x, y, z));
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

    private void UpdateOrCreatePlayer(Guid id, string name, Vector3 position)
    {
        if (!Players.TryGetValue(id, out var player))
        {
            // Create new player
            player = new RemotePlayer
            {
                id = id,
                name = name,
                pos = position,
                targetPos = position
            };

            player.go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.go.name = id == localPlayerId ? $"Local_{name}" : $"Remote_{name}";

            if (id == localPlayerId)
            {
                var renderer = player.go.GetComponent<Renderer>();
                if (renderer) renderer.material.color = Color.green;

                // Add local player controller
                if (player.go.GetComponent<LocalPlayerController>() == null)
                {
                    var controller = player.go.AddComponent<LocalPlayerController>();
                    controller.SetNetworkClient(this); // Pass UDP client reference
                }
            }

            Players[id] = player;
            Debug.Log($"[UdpNetworkClient] Created {(id == localPlayerId ? "LOCAL" : "REMOTE")} player {name}");
        }

        // Update position
        player.targetPos = position;
        
        // Handle local player reconciliation
        if (player.id == localPlayerId)
        {
            var controller = player.go.GetComponent<LocalPlayerController>();
            if (controller != null)
            {
                controller.ReceiveServerState(position, Time.time);
            }
        }

        player.lastUpdateTime = Time.time;
    }

    private void Update()
    {
        // Send heartbeat
        if (_connected && Time.time - _lastHeartbeat >= heartbeatInterval)
        {
            _ = SendHeartbeat();
            _lastHeartbeat = Time.time;
        }

        // Interpolate remote players
        foreach (var kv in Players)
        {
            var player = kv.Value;
            if (player.go == null || player.id == localPlayerId) continue;

            var current = player.go.transform.position;
            player.go.transform.position = Vector3.Lerp(current, player.targetPos, 10f * Time.deltaTime);
        }
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
        while (endIndex < src.Length && "0123456789.-".IndexOf(src[endIndex]) >= 0) endIndex++;
        var floatStr = src.Substring(startIndex, endIndex - startIndex);
        return float.TryParse(floatStr, out var f) ? f : 0f;
    }

    private async void OnApplicationQuit()
    {
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
        }
        catch { }
    }
}
