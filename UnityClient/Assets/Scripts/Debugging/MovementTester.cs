using UnityEngine;

/// <summary>
/// Quick testing utility for movement systems
/// Attach to any GameObject to test movement options
/// </summary>
public class MovementTester : MonoBehaviour
{
    [Header("Testing Controls")]
    [Tooltip("Press this key to toggle server reconciliation on/off")]
    public KeyCode toggleReconciliationKey = KeyCode.R;
    
    [Tooltip("Press this key to test teleportation (simulate large server correction)")]
    public KeyCode testTeleportKey = KeyCode.T;
    
    [Header("Debug Info")]
    public bool showDebugInfo = true;
    
    private LocalPlayerController _playerController;
    private bool _lastReconciliationState;

    private void Start()
    {
        _playerController = GetComponent<LocalPlayerController>();
        if (_playerController == null)
        {
            // Try to find it anywhere in the scene
            _playerController = FindFirstObjectByType<LocalPlayerController>();
        }
        
        if (_playerController == null)
        {
            Debug.LogWarning("[MovementTester] No LocalPlayerController found. Waiting for player spawn...");
            // Start a coroutine to keep looking
            StartCoroutine(WaitForPlayerController());
            return;
        }
        
        _lastReconciliationState = _playerController.enableServerReconciliation;
        Debug.Log("[MovementTester] Initialized. Controls:");
        Debug.Log($"  {toggleReconciliationKey} = Toggle Server Reconciliation");
        Debug.Log($"  {testTeleportKey} = Test Teleportation");
    }
    
    /// <summary>
    /// Keep looking for LocalPlayerController until found
    /// </summary>
    private System.Collections.IEnumerator WaitForPlayerController()
    {
        while (_playerController == null)
        {
            yield return new WaitForSeconds(0.5f);
            
            // Try to get from this GameObject first
            _playerController = GetComponent<LocalPlayerController>();
            
            // If not found, search scene
            if (_playerController == null)
            {
                _playerController = FindFirstObjectByType<LocalPlayerController>();
            }
            
            if (_playerController != null)
            {
                _lastReconciliationState = _playerController.enableServerReconciliation;
                Debug.Log("[MovementTester] Found LocalPlayerController! Debug controls now active.");
                yield break;
            }
        }
    }

    private void Update()
    {
        if (_playerController == null) return;
        
        // Toggle reconciliation
        if (Input.GetKeyDown(toggleReconciliationKey))
        {
            _playerController.enableServerReconciliation = !_playerController.enableServerReconciliation;
            Debug.Log($"[MovementTester] Server Reconciliation: {(_playerController.enableServerReconciliation ? "ENABLED" : "DISABLED")}");
        }
        
        // Test teleportation
        if (Input.GetKeyDown(testTeleportKey))
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-3f, 3f), 
                0, 
                Random.Range(-3f, 3f)
            );
            Vector3 testPosition = _playerController.transform.position + randomOffset;
            
            Debug.Log($"[MovementTester] Simulating server teleport to {testPosition}");
            _playerController.ReceiveServerState(testPosition, Time.time);
        }
        
        // Debug state changes
        if (_lastReconciliationState != _playerController.enableServerReconciliation)
        {
            _lastReconciliationState = _playerController.enableServerReconciliation;
            string mode = _lastReconciliationState ? "SERVER AUTHORITY" : "CLIENT PREDICTION";
            Debug.Log($"[MovementTester] Movement Mode: {mode}");
        }
    }

    private void OnGUI()
    {
        if (!showDebugInfo || _playerController == null) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("Movement Debug Info", GUI.skin.label);
        GUILayout.Space(5);
        
        // Current settings
        string reconciliationStatus = _playerController.enableServerReconciliation ? "ENABLED" : "DISABLED";
        Color reconciliationColor = _playerController.enableServerReconciliation ? Color.yellow : Color.green;
        
        GUI.color = reconciliationColor;
        GUILayout.Label($"Server Reconciliation: {reconciliationStatus}");
        GUI.color = Color.white;
        
        GUILayout.Label($"Position: {_playerController.transform.position:F2}");
        GUILayout.Label($"Input Send Rate: {_playerController.inputSendRate}Hz");
        
        GUILayout.Space(10);
        GUILayout.Label("Controls:", GUI.skin.label);
        GUILayout.Label($"[{toggleReconciliationKey}] Toggle Reconciliation");
        GUILayout.Label($"[{testTeleportKey}] Test Teleport");
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
