using UnityEngine;

/// <summary>
/// Debug Manager - Place this in your scene to automatically manage debug tools
/// This will automatically attach MovementTester to spawned players
/// </summary>
public class DebugManager : MonoBehaviour
{
    [Header("Auto-attach Settings")]
    [Tooltip("Automatically attach MovementTester to local player")]
    public bool autoAttachMovementTester = true;
    
    [Tooltip("Check for new players every X seconds")]
    public float playerCheckInterval = 1f;
    
    [Header("Debug Controls")]
    [Tooltip("Global toggle key for all debug features")]
    public KeyCode globalDebugToggle = KeyCode.F1;
    
    [Header("Visual Debug")]
    public bool showPlayerLabels = true;
    public bool showConnectionInfo = true;
    
    private float _lastPlayerCheck = 0f;
    private bool _debugEnabled = true;
    
    private void Start()
    {
        Debug.Log("[DebugManager] Initialized - Managing debug tools for runtime spawned players");
        Debug.Log($"[DebugManager] Press {globalDebugToggle} to toggle all debug features");
    }
    
    private void Update()
    {
        // Global debug toggle
        if (Input.GetKeyDown(globalDebugToggle))
        {
            ToggleDebugMode();
        }
        
        // Periodic player check
        if (Time.time - _lastPlayerCheck >= playerCheckInterval)
        {
            CheckForNewPlayers();
            _lastPlayerCheck = Time.time;
        }
    }
    
    private void CheckForNewPlayers()
    {
        if (!autoAttachMovementTester) return;
        
        // Find all LocalPlayerController instances without MovementTester
        var controllers = FindObjectsByType<LocalPlayerController>(FindObjectsSortMode.None);
        
        foreach (var controller in controllers)
        {
            if (controller.GetComponent<MovementTester>() == null)
            {
                var tester = controller.gameObject.AddComponent<MovementTester>();
                tester.showDebugInfo = _debugEnabled;
                
                Debug.Log($"[DebugManager] Attached MovementTester to {controller.gameObject.name}");
            }
        }
    }
    
    private void ToggleDebugMode()
    {
        _debugEnabled = !_debugEnabled;
        
        // Update all MovementTester instances
        var testers = FindObjectsByType<MovementTester>(FindObjectsSortMode.None);
        foreach (var tester in testers)
        {
            tester.showDebugInfo = _debugEnabled;
        }
        
        Debug.Log($"[DebugManager] Debug Mode: {(_debugEnabled ? "ENABLED" : "DISABLED")}");
    }
    
    private void OnGUI()
    {
        if (!showConnectionInfo) return;
        
        // Show connection info in top-right corner
        var rect = new Rect(Screen.width - 250, 10, 240, 100);
        GUI.BeginGroup(rect);
        GUI.Box(new Rect(0, 0, 240, 100), "Debug Info");
        
        var networkClient = UdpNetworkClient.Instance;
        if (networkClient != null)
        {
            GUI.Label(new Rect(10, 25, 220, 20), $"Players: {networkClient.Players.Count}");
            GUI.Label(new Rect(10, 45, 220, 20), $"Local ID: {networkClient.localPlayerId.ToString().Substring(0, 8)}...");
            GUI.Label(new Rect(10, 65, 220, 20), $"Debug: {(_debugEnabled ? "ON" : "OFF")} ({globalDebugToggle})");
        }
        else
        {
            GUI.Label(new Rect(10, 25, 220, 20), "UdpNetworkClient not found");
        }
        
        GUI.EndGroup();
        
        if (showPlayerLabels)
        {
            ShowPlayerLabels();
        }
    }
    
    private void ShowPlayerLabels()
    {
        var networkClient = UdpNetworkClient.Instance;
        if (networkClient == null) return;
        
        foreach (var player in networkClient.Players.Values)
        {
            if (player.go == null) continue;
            
            Vector3 screenPos = Camera.main.WorldToScreenPoint(player.go.transform.position);
            if (screenPos.z > 0 && screenPos.x > 0 && screenPos.x < Screen.width && 
                screenPos.y > 0 && screenPos.y < Screen.height)
            {
                screenPos.y = Screen.height - screenPos.y; // Flip Y coordinate
                
                string label = player.id == networkClient.localPlayerId ? 
                    $"🎮 {player.name} (YOU)" : 
                    $"👤 {player.name}";
                
                var labelRect = new Rect(screenPos.x - 50, screenPos.y - 30, 100, 20);
                
                // Background for readability
                GUI.color = new Color(0, 0, 0, 0.5f);
                GUI.Box(labelRect, "");
                GUI.color = player.id == networkClient.localPlayerId ? Color.green : Color.white;
                GUI.Label(labelRect, label);
                GUI.color = Color.white;
            }
        }
    }
}
