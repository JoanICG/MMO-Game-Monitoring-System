using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using UnityEngine;

// Basic WebSocket network client.
// Attach to an empty GameObject (e.g., NetworkClientRoot) and mark as DontDestroyOnLoad.
public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance;

    [Header("Connection")] public string host = "ws://localhost:8080/ws"; // Docker backend
    public string playerName = "Player";

    [Header("Debug")] public bool autoJoin = true;
    public Guid localPlayerId;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;

    // Remote players
    public readonly Dictionary<Guid, RemotePlayer> Players = new();

    [Serializable]
    private class JoinMsg { public string op = "join"; public string name; public JoinMsg(string n){ name = n; } }

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
            Debug.Log($"[NetworkClient] Awake autoJoin. Host={host} Name={playerName}");
            await ConnectAndJoin();
            // Start fallback spawn check
            StartCoroutine(FallbackLocalSpawn());
        }
    }

    public async Task ConnectAndJoin()
    {
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        try
        {
            Debug.Log("[NetworkClient] Connecting...");
            await _ws.ConnectAsync(new Uri(host), _cts.Token);
            Debug.Log("[NetworkClient] Connected, sending join");
            await SendJoin();
            _ = ReceiveLoop();
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocket connect error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send player input with sequence for client-side prediction
    /// </summary>
    public async Task SendPlayerInput(uint sequence, Vector2 input, float speed)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        
        // Enhanced input packet with sequence for reconciliation
        var json = $"{{\"op\":\"input\",\"seq\":{sequence},\"x\":{input.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"y\":{input.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"speed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"time\":{Time.time.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        await SendRaw(json);
    }

    /// <summary>
    /// Legacy method for backward compatibility
    /// </summary>
    public async Task SendInput(Vector3 input, float speed)
    {
        await SendPlayerInput(0, new Vector2(input.x, input.z), speed);
    }

    private async Task SendJoin()
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var msg = new JoinMsg(playerName);
        // Use JsonUtility now that it's a proper serializable class
        var json = JsonUtility.ToJson(msg);
        await SendRaw(json);
        Debug.Log($"[NetworkClient] Join JSON sent: {json}");
    }

    private async Task SendRaw(string json)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token); }
        catch (Exception ex) { Debug.LogWarning($"Send failed: {ex.Message}"); }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[8192];
        while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(buffer, _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Receive error: {ex.Message}");
                break;
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Debug.Log($"[NetworkClient] Msg: {json}");
            HandleMessage(json);
        }
    }

    private void HandleMessage(string json)
    {
        if (json.Contains("\"op\":\"join_ack\""))
        {
            var idStr = ExtractAfter(json, "\"id\":\"", 36);
            if (Guid.TryParse(idStr, out var gid))
            {
                localPlayerId = gid;
                Debug.Log($"Assigned local id {localPlayerId}");
                // Ensure already spawned object (from an early state) is colored green
                if (Players.TryGetValue(localPlayerId, out var rp) && rp.go)
                {
                    var r = rp.go.GetComponent<Renderer>();
                    if (r) r.material.color = Color.green;
                }
            }
        }
        else if (json.Contains("\"op\":\"state\""))
        {
            // simple naive parse
            var playersIndex = json.IndexOf("\"players\":[", StringComparison.Ordinal);
            if (playersIndex < 0) return;
            var arrayPart = json[(playersIndex + 11)..];
            arrayPart = arrayPart.TrimEnd('}', '\n', '\r');

            // Clear not reused (we respawn naive for simplicity)
            var keep = new HashSet<Guid>();
            foreach (var chunk in arrayPart.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idStr = ExtractAfter(chunk, "\"id\":\"", 36);
                if (!Guid.TryParse(idStr, out var id)) continue;
                var name = ExtractString(chunk, "\"name\":\"");
                var x = ExtractFloat(chunk, "\"x\":");
                var y = ExtractFloat(chunk, "\"y\":");
                var z = ExtractFloat(chunk, "\"z\":");
                keep.Add(id);
                if (!Players.TryGetValue(id, out var rp))
                {
                    var initial = new Vector3(x, y, z);
                    rp = new RemotePlayer { id = id, name = name, pos = initial, targetPos = initial };
                    rp.go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    rp.go.name = id == localPlayerId ? $"Local_{name}" : $"Remote_{name}";
                    if (id == localPlayerId)
                    {
                        var r = rp.go.GetComponent<Renderer>();
                        if (r) r.material.color = Color.green;
                        
                        // Auto-attach controller if not present so WASD moves the spawned capsule
                        if (rp.go.GetComponent<LocalPlayerController>() == null)
                        {
                            rp.go.AddComponent<LocalPlayerController>();
                        }
                        
                        // Auto-attach movement tester for debugging
                        if (rp.go.GetComponent<MovementTester>() == null)
                        {
                            var tester = rp.go.AddComponent<MovementTester>();
                            Debug.Log("[NetworkClient] MovementTester attached to local player - Press R to toggle reconciliation, T to test teleport");
                        }
                    }
                    Debug.Log($"[NetworkClient] Spawn {(id==localPlayerId?"LOCAL":"REMOTE")} id={id} name={name} pos=({x},{y},{z})");
                    Players[id] = rp;
                }
                // Update position data
                var newPos = new Vector3(x, y, z);
                rp.targetPos = newPos;
                
                // For local player, handle based on reconciliation settings
                if (rp.id == localPlayerId)
                {
                    rp.pos = newPos; // store server pos
                    
                    // Tell LocalPlayerController about server position
                    var controller = rp.go.GetComponent<LocalPlayerController>();
                    if (controller != null)
                    {
                        controller.ReceiveServerState(newPos, Time.time);
                    }
                    else
                    {
                        // Fallback: directly set position if no controller
                        Debug.LogWarning($"[NetworkClient] LocalPlayerController NOT FOUND, setting position directly");
                        rp.go.transform.position = newPos;
                    }
                }
                else
                {
                    // optional: on big teleport snap directly if distance large
                    if ((rp.go.transform.position - newPos).sqrMagnitude > 25f)
                    {
                        rp.go.transform.position = newPos;
                    }
                }
                rp.lastUpdateTime = Time.time;
            }
            // Remove disconnected
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
    }

    private string ExtractAfter(string src, string marker, int length)
    {
        var i = src.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return string.Empty;
        var start = i + marker.Length;
        var take = Math.Min(length, src.Length - start);
        return src.Substring(start, take);
    }
    private string ExtractString(string src, string marker)
    {
        var i = src.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return string.Empty;
        var start = i + marker.Length;
        var end = src.IndexOf('"', start);
        if (end < 0) return string.Empty;
        return src.Substring(start, end - start);
    }
    private float ExtractFloat(string src, string marker)
    {
        var i = src.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return 0f;
        var start = i + marker.Length;
        int end = start;
        while (end < src.Length && "0123456789.-".IndexOf(src[end]) >= 0) end++;
        var slice = src.Substring(start, end - start);
        return float.TryParse(slice, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var f)
            ? f
            : 0f;
    }

    private async void OnApplicationQuit()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "quit", CancellationToken.None);
            }
        }
        catch { }
    }

    // If after a short delay we have a local id but no local GameObject (or no id and no snapshot), spawn placeholder.
    private System.Collections.IEnumerator FallbackLocalSpawn()
    {
        yield return new WaitForSeconds(2f);
        if (localPlayerId != Guid.Empty)
        {
            if (!Players.TryGetValue(localPlayerId, out var rp) || rp.go == null)
            {
                Debug.LogWarning("[NetworkClient] Fallback spawn: local player not present after 2s, creating placeholder");
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Local_{playerName}_Fallback";
                var rend = go.GetComponent<Renderer>();
                if (rend) rend.material.color = Color.green;
                var newRp = new RemotePlayer { id = localPlayerId, name = playerName, go = go, pos = Vector3.zero, targetPos = Vector3.zero };
                Players[localPlayerId] = newRp;
            }
        }
        else
        {
            Debug.LogWarning("[NetworkClient] No localPlayerId after 2s (no join_ack). Check server or network.");
        }
    }

    private void Update()
    {
        // Interpolate remote players toward targetPos
        foreach (var kv in Players)
        {
            var rp = kv.Value;
            if (rp.go == null) continue;
            if (rp.id == localPlayerId) continue; // local controlled by input
            var current = rp.go.transform.position;
            // Smooth factor (tune): more deltaTime * factor -> faster convergence
            rp.go.transform.position = Vector3.Lerp(current, rp.targetPos, 10f * Time.deltaTime);
        }
    }
}
