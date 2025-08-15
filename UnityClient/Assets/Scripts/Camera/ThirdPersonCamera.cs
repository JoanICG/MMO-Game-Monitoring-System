using UnityEngine;

/// <summary>
/// Professional Third-Person Camera for MMO
/// Features: Smooth following, collision detection, orbit controls
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target; // El jugador que seguirá la cámara
    public bool autoFindPlayer = true; // Auto-detectar jugador local
    public Vector3 offset = new Vector3(0, 5, -8); // Offset desde el target
    
    [Header("Camera Controls")]
    public float mouseSensitivity = 2f;
    public float scrollSensitivity = 2f;
    public bool invertY = false;
    
    [Header("Camera Limits")]
    public float minDistance = 3f;
    public float maxDistance = 15f;
    public float minVerticalAngle = -30f;
    public float maxVerticalAngle = 60f;
    
    [Header("Smoothing")]
    public float positionSmoothTime = 0.3f;
    public float rotationSmoothTime = 0.1f;
    
    [Header("Collision")]
    public LayerMask collisionLayers = 1; // What can block the camera
    public float collisionRadius = 0.3f;
    
    // Internal state
    private float _currentDistance;
    private float _targetDistance;
    private float _horizontalAngle = 0f;
    private float _verticalAngle = 0f;
    
    // Smoothing
    private Vector3 _currentPosition;
    private Vector3 _targetPosition;
    private Vector3 _positionVelocity;
    private Vector3 _rotationVelocity;
    
    private void Start()
    {
        // Initialize camera position
        if (target != null)
        {
            _currentDistance = _targetDistance = Vector3.Distance(transform.position, target.position);
        }
        else
        {
            _currentDistance = _targetDistance = Vector3.Distance(Vector3.zero, offset);
        }
        
        _currentPosition = transform.position;
        
        // Lock cursor for camera control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        Debug.Log("[ThirdPersonCamera] Initialized - Use mouse to look around, scroll to zoom");
        
        // Auto-find player if enabled and no target set
        if (autoFindPlayer && target == null)
        {
            StartCoroutine(AutoFindPlayerRoutine());
        }
    }
    
    /// <summary>
    /// Coroutine that continuously looks for the local player
    /// </summary>
    private System.Collections.IEnumerator AutoFindPlayerRoutine()
    {
        Debug.Log("[ThirdPersonCamera] Searching for local player...");
        
        while (target == null)
        {
            // Look for LocalPlayerController
            var playerController = FindFirstObjectByType<LocalPlayerController>();
            if (playerController != null)
            {
                SetTarget(playerController.transform);
                Debug.Log($"[ThirdPersonCamera] Auto-found local player: {playerController.name}");
                yield break; // Exit coroutine
            }
            
            // Wait a bit before trying again
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    private void LateUpdate()
    {
        if (target == null) return;
        
        HandleInput();
        CalculateTargetPosition();
        HandleCollision();
        ApplySmoothing();
        UpdateCameraTransform();
    }
    
    private void HandleInput()
    {
        // Mouse look
        if (Input.GetMouseButton(1)) // Right click to rotate
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            
            _horizontalAngle += mouseX;
            _verticalAngle += invertY ? mouseY : -mouseY;
            
            // Clamp vertical angle
            _verticalAngle = Mathf.Clamp(_verticalAngle, minVerticalAngle, maxVerticalAngle);
        }
        
        // Zoom with scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel") * scrollSensitivity;
        _targetDistance = Mathf.Clamp(_targetDistance - scroll, minDistance, maxDistance);
        
        // ESC to toggle cursor
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleCursor();
        }
    }
    
    private void CalculateTargetPosition()
    {
        // Calculate rotation from angles
        Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0);
        
        // Calculate ideal position
        Vector3 direction = rotation * Vector3.back;
        _targetPosition = target.position + direction * _targetDistance;
        _currentDistance = _targetDistance;
    }
    
    private void HandleCollision()
    {
        // Raycast from target to camera to check for obstacles
        Vector3 directionToCamera = (_targetPosition - target.position).normalized;
        float targetDistance = Vector3.Distance(target.position, _targetPosition);
        
        RaycastHit hit;
        if (Physics.SphereCast(target.position, collisionRadius, directionToCamera, out hit, targetDistance, collisionLayers))
        {
            // Move camera closer to avoid collision
            float safeDistance = hit.distance - collisionRadius * 2f;
            safeDistance = Mathf.Max(safeDistance, minDistance);
            
            _targetPosition = target.position + directionToCamera * safeDistance;
            _currentDistance = safeDistance;
        }
    }
    
    private void ApplySmoothing()
    {
        // Smooth position movement
        _currentPosition = Vector3.SmoothDamp(_currentPosition, _targetPosition, ref _positionVelocity, positionSmoothTime);
    }
    
    private void UpdateCameraTransform()
    {
        // Set camera position
        transform.position = _currentPosition;
        
        // Look at target with smooth rotation
        Vector3 lookDirection = target.position - transform.position;
        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime / rotationSmoothTime);
        }
    }
    
    private void ToggleCursor()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
    
    /// <summary>
    /// Set new target for camera to follow
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            _currentPosition = target.position + offset;
            transform.position = _currentPosition;
        }
    }
    
    /// <summary>
    /// Reset camera to default position behind target
    /// </summary>
    public void ResetCamera()
    {
        _horizontalAngle = 0f;
        _verticalAngle = 0f;
        _targetDistance = Vector3.Distance(Vector3.zero, offset);
    }
    
    // Gizmos for debugging
    private void OnDrawGizmosSelected()
    {
        if (target != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(target.position, minDistance);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(target.position, maxDistance);
            
            // Draw collision sphere
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, collisionRadius);
        }
    }
}
