using UnityEngine;

/// <summary>
/// Camera Manager - Handles switching between different camera modes
/// Perfect for MMO with multiple camera types
/// </summary>
public class CameraManager : MonoBehaviour
{
    [Header("Camera Components")]
    public ThirdPersonCamera thirdPersonCamera;
    public FreeCamera freeCamera;
    public Camera mainCamera;
    
    [Header("Settings")]
    public CameraMode defaultMode = CameraMode.ThirdPerson;
    public KeyCode switchCameraKey = KeyCode.C;
    public bool autoFindPlayer = true; // Auto-detectar jugador cuando aparezca
    
    // Current state
    private CameraMode _currentMode;
    private Transform _playerTarget;
    
    public enum CameraMode
    {
        ThirdPerson,
        Free,
        FirstPerson // For future implementation
    }
    
    private void Start()
    {
        InitializeCameras();
        SetCameraMode(defaultMode);
        
        // Start looking for player if auto-find is enabled
        if (autoFindPlayer)
        {
            StartCoroutine(AutoFindPlayerRoutine());
        }
        
        Debug.Log($"[CameraManager] Initialized with {defaultMode} mode. Press {switchCameraKey} to switch cameras");
    }
    
    private void Update()
    {
        HandleCameraSwitching();
    }
    
    /// <summary>
    /// Continuously look for the local player until found
    /// </summary>
    private System.Collections.IEnumerator AutoFindPlayerRoutine()
    {
        Debug.Log("[CameraManager] Auto-searching for local player...");
        
        while (_playerTarget == null)
        {
            FindPlayerTarget();
            
            if (_playerTarget == null)
            {
                // Wait before trying again
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Debug.Log("[CameraManager] Player found and connected to camera system!");
                yield break; // Exit coroutine
            }
        }
    }
    
    private void InitializeCameras()
    {
        // Ensure we have all required components
        if (thirdPersonCamera == null)
            thirdPersonCamera = GetComponent<ThirdPersonCamera>();
        
        if (freeCamera == null)
            freeCamera = GetComponent<FreeCamera>();
        
        if (mainCamera == null)
            mainCamera = GetComponent<Camera>();
        
        // Find player target automatically
        FindPlayerTarget();
    }
    
    private void FindPlayerTarget()
    {
        // Look for LocalPlayerController in the scene (using new Unity API)
        var playerController = FindFirstObjectByType<LocalPlayerController>();
        if (playerController != null)
        {
            SetPlayerTarget(playerController.transform);
            Debug.Log($"[CameraManager] Found player target: {playerController.name}");
        }
        else
        {
            Debug.LogWarning("[CameraManager] No LocalPlayerController found. Camera will not follow player.");
        }
    }
    
    private void HandleCameraSwitching()
    {
        if (Input.GetKeyDown(switchCameraKey))
        {
            SwitchToNextCamera();
        }
        
        // Quick switch keys
        if (Input.GetKeyDown(KeyCode.Alpha1))
            SetCameraMode(CameraMode.ThirdPerson);
        if (Input.GetKeyDown(KeyCode.Alpha2))
            SetCameraMode(CameraMode.Free);
    }
    
    public void SwitchToNextCamera()
    {
        CameraMode nextMode = (CameraMode)(((int)_currentMode + 1) % System.Enum.GetValues(typeof(CameraMode)).Length);
        SetCameraMode(nextMode);
    }
    
    public void SetCameraMode(CameraMode mode)
    {
        // Disable all cameras first
        DisableAllCameras();
        
        _currentMode = mode;
        
        switch (mode)
        {
            case CameraMode.ThirdPerson:
                EnableThirdPersonCamera();
                break;
            case CameraMode.Free:
                EnableFreeCamera();
                break;
            case CameraMode.FirstPerson:
                // TODO: Implement first person camera
                Debug.LogWarning("[CameraManager] First person camera not implemented yet");
                SetCameraMode(CameraMode.ThirdPerson);
                break;
        }
        
        Debug.Log($"[CameraManager] Switched to {mode} camera mode");
    }
    
    private void DisableAllCameras()
    {
        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = false;
        
        if (freeCamera != null)
        {
            freeCamera.enabled = false;
            freeCamera.SetActive(false);
        }
    }
    
    private void EnableThirdPersonCamera()
    {
        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.enabled = true;
            if (_playerTarget != null)
                thirdPersonCamera.SetTarget(_playerTarget);
        }
        else
        {
            Debug.LogError("[CameraManager] ThirdPersonCamera component not found!");
        }
    }
    
    private void EnableFreeCamera()
    {
        if (freeCamera != null)
        {
            freeCamera.enabled = true;
            freeCamera.SetActive(true);
        }
        else
        {
            Debug.LogError("[CameraManager] FreeCamera component not found!");
        }
    }
    
    /// <summary>
    /// Set the player target for cameras that need to follow
    /// </summary>
    public void SetPlayerTarget(Transform target)
    {
        _playerTarget = target;
        
        // Update current camera if it's third person
        if (_currentMode == CameraMode.ThirdPerson && thirdPersonCamera != null)
        {
            thirdPersonCamera.SetTarget(target);
        }
        
        Debug.Log($"[CameraManager] Player target set to: {target.name}");
    }
    
    /// <summary>
    /// Get current camera mode
    /// </summary>
    public CameraMode GetCurrentMode()
    {
        return _currentMode;
    }
    
    /// <summary>
    /// Check if current camera is following the player
    /// </summary>
    public bool IsFollowingPlayer()
    {
        return _currentMode == CameraMode.ThirdPerson && _playerTarget != null;
    }
    
    // For debugging
    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 100));
        GUILayout.Label($"Camera Mode: {_currentMode}");
        GUILayout.Label($"Switch Key: {switchCameraKey}");
        GUILayout.Label("Quick: 1=Third Person, 2=Free");
        GUILayout.EndArea();
    }
}
