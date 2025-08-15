using UnityEngine;

/// <summary>
/// Free Spectator Camera - Great for debugging and admin mode
/// Features: WASD movement, mouse look, speed controls
/// </summary>
public class FreeCamera : MonoBehaviour
{
    [Header("Movement")]
    public float normalSpeed = 10f;
    public float fastSpeed = 20f;
    public float slowSpeed = 2f;
    
    [Header("Look")]
    public float mouseSensitivity = 2f;
    public bool invertY = false;
    
    [Header("Smoothing")]
    public float movementSmoothTime = 0.1f;
    public float lookSmoothTime = 0.05f;
    
    // Internal state
    private Vector3 _velocity = Vector3.zero;
    private Vector3 _currentVelocity = Vector3.zero;
    private Vector3 _smoothDampVelocity = Vector3.zero;
    private float _rotationX = 0f;
    private float _rotationY = 0f;
    private float _targetRotationX = 0f;
    private float _targetRotationY = 0f;
    private float _rotationVelocityX = 0f;
    private float _rotationVelocityY = 0f;
    
    private bool _isActive = false;
    
    private void Start()
    {
        Debug.Log("[FreeCamera] Controls: WASD = move, Mouse = look, Shift = fast, Ctrl = slow, F = toggle");
    }
    
    private void Update()
    {
        HandleToggle();
        
        if (!_isActive) return;
        
        HandleMovement();
        HandleLook();
        ApplySmoothing();
    }
    
    private void HandleToggle()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            ToggleCamera();
        }
    }
    
    private void HandleMovement()
    {
        // Get input
        Vector3 input = Vector3.zero;
        
        if (Input.GetKey(KeyCode.W)) input += transform.forward;
        if (Input.GetKey(KeyCode.S)) input -= transform.forward;
        if (Input.GetKey(KeyCode.A)) input -= transform.right;
        if (Input.GetKey(KeyCode.D)) input += transform.right;
        if (Input.GetKey(KeyCode.Q)) input -= transform.up;
        if (Input.GetKey(KeyCode.E)) input += transform.up;
        
        // Normalize input
        input = input.normalized;
        
        // Apply speed modifiers
        float currentSpeed = normalSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) currentSpeed = fastSpeed;
        if (Input.GetKey(KeyCode.LeftControl)) currentSpeed = slowSpeed;
        
        // Set target velocity
        _velocity = input * currentSpeed;
    }
    
    private void HandleLook()
    {
        if (!Input.GetMouseButton(1)) return; // Only look when right-clicking
        
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        _targetRotationY += mouseX;
        _targetRotationX += invertY ? mouseY : -mouseY;
        
        // Clamp vertical rotation
        _targetRotationX = Mathf.Clamp(_targetRotationX, -90f, 90f);
    }
    
    private void ApplySmoothing()
    {
        // Smooth movement - Fix NaN issues
        _currentVelocity = Vector3.SmoothDamp(_currentVelocity, _velocity, ref _smoothDampVelocity, movementSmoothTime);
        
        // Validate the position before applying it
        Vector3 newPosition = transform.position + _currentVelocity * Time.deltaTime;
        if (!float.IsNaN(newPosition.x) && !float.IsNaN(newPosition.y) && !float.IsNaN(newPosition.z))
        {
            transform.position = newPosition;
        }
        else
        {
            Debug.LogWarning("[FreeCamera] Invalid position detected, skipping frame");
        }
        
        // Smooth rotation
        _rotationX = Mathf.SmoothDampAngle(_rotationX, _targetRotationX, ref _rotationVelocityX, lookSmoothTime);
        _rotationY = Mathf.SmoothDampAngle(_rotationY, _targetRotationY, ref _rotationVelocityY, lookSmoothTime);
        
        // Validate rotation before applying
        if (!float.IsNaN(_rotationX) && !float.IsNaN(_rotationY))
        {
            transform.rotation = Quaternion.Euler(_rotationX, _rotationY, 0f);
        }
        else
        {
            Debug.LogWarning("[FreeCamera] Invalid rotation detected, skipping frame");
        }
    }
    
    public void ToggleCamera()
    {
        _isActive = !_isActive;
        
        if (_isActive)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("[FreeCamera] Activated - Right-click and drag to look around");
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[FreeCamera] Deactivated");
        }
    }
    
    public void SetActive(bool active)
    {
        _isActive = active;
        if (active)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
    
    private void OnEnable()
    {
        // Initialize rotation to current transform safely
        Vector3 currentEuler = transform.eulerAngles;
        
        // Normalize angles to prevent NaN issues
        _rotationX = _targetRotationX = NormalizeAngle(currentEuler.x);
        _rotationY = _targetRotationY = NormalizeAngle(currentEuler.y);
        
        // Initialize velocities to zero
        _velocity = Vector3.zero;
        _currentVelocity = Vector3.zero;
        _smoothDampVelocity = Vector3.zero;
        _rotationVelocityX = 0f;
        _rotationVelocityY = 0f;
        
        Debug.Log($"[FreeCamera] Initialized at position: {transform.position}, rotation: ({_rotationX}, {_rotationY})");
    }
    
    /// <summary>
    /// Normalize angle to [-180, 180] range to prevent issues
    /// </summary>
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
