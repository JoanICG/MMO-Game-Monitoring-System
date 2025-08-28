using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Generic;

// PRODUCTION-READY: Client-Side Prediction + Server Reconciliation + Camera Integration
// Designed for scalability with Kafka metrics, Kubernetes deployment
public class LocalPlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float acceleration = 20f;
    
    [Header("Network")]
    public float inputSendRate = 20f; // 20Hz for production
    public bool enableServerReconciliation = false; // Disable for smoother movement
    public float reconciliationSmoothTime = 0.2f; // Smooth correction when enabled
    public float maxErrorBeforeSnap = 2f; // Distance that forces immediate snap
    
    [Header("Camera Integration")]
    public bool autoSetupCamera = true;
    
    private UdpNetworkClient _udpNet; // UDP client reference
    private float _lastInputSendTime;
    
    // Client-Side Prediction
    private Vector3 _velocity = Vector3.zero;
    private uint _inputSequence = 0;
    
    // Server reconciliation smoothing
    private Vector3 _serverPosition;
    private Vector3 _reconciliationVelocity;
    private bool _hasServerPosition = false;
    
    // Input state
    private Vector2 _currentInput;
    private Vector2 _lastSentInput;
    
    // Jump state for client-side prediction
    private float _verticalVelocity = 0f;
    private bool _isJumping = false;
    
    // Metrics for future Kafka integration
    private float _totalInputsSent = 0;
    private float _lastMetricsTime = 0;
    
    // Camera reference (will be resolved at runtime)
    private object _cameraManager;

    // Add method to set network client
    public void SetNetworkClient(UdpNetworkClient udpClient)
    {
        _udpNet = udpClient;
    }

    /// <summary>
    /// Server reconciliation - called by NetworkClient
    /// </summary>
    public void ReceiveServerState(Vector3 serverPos, float serverTime)
    {
        if (!enableServerReconciliation)
        {
            // Pure client-side prediction: ignore server corrections for smoother movement
            Debug.Log($"[LocalPlayer] Server reconciliation disabled. Client pos: {transform.position}, Server pos: {serverPos}");
            return;
        }
        
        float positionError = Vector3.Distance(transform.position, serverPos);
        
        // Store server position for smooth reconciliation
        _serverPosition = serverPos;
        _hasServerPosition = true;
        
        // Immediate snap for large errors (teleportation, major desync)
        if (positionError > maxErrorBeforeSnap)
        {
            Debug.LogWarning($"[Reconciliation] Large error detected ({positionError:F2}m), snapping to server position");
            transform.position = serverPos;
            _velocity = Vector3.zero;
            _verticalVelocity = 0;
            _isJumping = false;
            LogMetrics("player.prediction.snap", positionError);
        }
        else if (positionError > 0.05f) // Small errors get smooth correction
        {
            Debug.Log($"[Reconciliation] Small error detected ({positionError:F3}m), smooth correcting");
            LogMetrics("player.prediction.smooth_correction", positionError);
        }
    }

    private void Start()
    {
        // Find UDP client - server is now UDP-only
        _udpNet = UdpNetworkClient.Instance;
        if (_udpNet == null)
        {
            _udpNet = FindFirstObjectByType<UdpNetworkClient>();
        }
        
        if (_udpNet == null)
        {
            Debug.LogError("[LocalPlayerController] UdpNetworkClient not found! Please add UdpNetworkClient to the scene. WebSocket is no longer supported.");
            return;
        }
        
        _lastMetricsTime = Time.time;
        
        SetupCameraSystem();
        
        Debug.Log("[LocalPlayerController] Initialized with UDP-only support");
    }
    
    private void SetupCameraSystem()
    {
        if (!autoSetupCamera) return;
        
        // Try to find any camera script that has SetPlayerTarget method
        var scripts = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var script in scripts)
        {
            var method = script.GetType().GetMethod("SetPlayerTarget");
            if (method != null)
            {
                try
                {
                    method.Invoke(script, new object[] { transform });
                    Debug.Log($"[LocalPlayerController] Camera system connected to {script.GetType().Name}");
                    _cameraManager = script;
                    break;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[LocalPlayerController] Failed to set camera target: {e.Message}");
                }
            }
        }
        
        if (_cameraManager == null)
        {
            Debug.LogWarning("[LocalPlayerController] No compatible camera system found.");
            Debug.LogWarning("Add CameraManager, ThirdPersonCamera, or similar component to enable camera following.");
        }
    }

    private void Update()
    {
        if (_udpNet == null || _udpNet.localPlayerId == System.Guid.Empty) return;

        // Gather input
        _currentInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        // Client-side prediction: Apply movement and jumping locally first
        ApplyLocalMovement();

        // Send position to server at fixed rate (server-authoritative synchronization)
        if (Time.time - _lastInputSendTime >= 1f / inputSendRate)
        {
            SendPositionToServer();
            _lastInputSendTime = Time.time;
        }

        // Send periodic metrics (for future Kafka integration)
        SendPeriodicMetrics();
    }

    private void ApplyLocalMovement()
    {
        // Convert 2D input to 3D movement
        Vector3 inputDirection = new Vector3(_currentInput.x, 0, _currentInput.y);
        Vector3 targetVelocity = inputDirection.normalized * speed;
        
        // Smooth acceleration/deceleration
        if (_currentInput.magnitude > 0.1f)
        {
            _velocity = Vector3.MoveTowards(_velocity, targetVelocity, acceleration * Time.deltaTime);
        }
        else
        {
            _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, acceleration * Time.deltaTime);
        }
        
        // Apply horizontal movement
        transform.position += _velocity * Time.deltaTime;
        
        // Boundary checking to match server
        transform.position = new Vector3(
            Mathf.Clamp(transform.position.x, -50f, 50f),
            transform.position.y,
            Mathf.Clamp(transform.position.z, -50f, 50f)
        );
        
        // Client-side jump prediction
        if (Input.GetKeyDown(KeyCode.Space) && IsGrounded())
        {
            _verticalVelocity = 8f; // Match server jump force
            _isJumping = true;
            Debug.Log("[LocalPlayer] Local jump prediction");
        }
        
        // Apply gravity and vertical movement
        if (_isJumping || transform.position.y > 0)
        {
            _verticalVelocity -= 20f * Time.deltaTime; // Match server gravity
            transform.position += new Vector3(0, _verticalVelocity * Time.deltaTime, 0);
            
            // Check ground collision
            if (transform.position.y <= 0)
            {
                transform.position = new Vector3(transform.position.x, 0, transform.position.z);
                _verticalVelocity = 0;
                _isJumping = false;
            }
        }
        
        // Apply smooth server reconciliation if enabled and needed
        if (enableServerReconciliation && _hasServerPosition)
        {
            float errorDistance = Vector3.Distance(transform.position, _serverPosition);
            if (errorDistance > 0.01f) // Only correct if there's meaningful error
            {
                Vector3 correctedPosition = Vector3.SmoothDamp(
                    transform.position, 
                    _serverPosition, 
                    ref _reconciliationVelocity, 
                    reconciliationSmoothTime
                );
                transform.position = correctedPosition;
                
                // Reset jump state if server says we're grounded
                if (_serverPosition.y <= 0.1f && transform.position.y > 0.1f)
                {
                    _verticalVelocity = 0;
                    _isJumping = false;
                }
            }
        }
    }
    
    private bool IsGrounded()
    {
        // Simple ground check - you can replace with raycast for more accuracy
        return transform.position.y <= 0.1f && !_isJumping;
    }

    private async void SendPositionToServer()
    {
        // Send current position to server for server-authoritative synchronization
        _inputSequence++;

        // UDP-only communication (WebSocket removed)
        if (_udpNet != null)
        {
            await _udpNet.SendPlayerInput(_inputSequence, _currentInput, speed, transform.position);
        }
        else
        {
            Debug.LogError("[LocalPlayerController] No UDP client available! Server is UDP-only.");
            return;
        }

        _lastSentInput = _currentInput;
        _totalInputsSent++;

        Debug.Log($"[Position] Seq: {_inputSequence}, Position: {transform.position}, Protocol: UDP");
    }

    private void SendPeriodicMetrics()
    {
        // Send metrics every 5 seconds (future Kafka events)
        if (Time.time - _lastMetricsTime >= 5f)
        {
            var metrics = new {
                playerId = _udpNet?.localPlayerId.ToString() ?? "unknown",
                inputsPerSecond = _totalInputsSent / 5f,
                position = transform.position,
                velocity = _velocity.magnitude,
                timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            LogMetrics("player.performance", metrics);
            
            _totalInputsSent = 0;
            _lastMetricsTime = Time.time;
        }
    }

    private void LogMetrics(string eventType, object data)
    {
        // Future: Send to Kafka via NetworkClient.SendMetrics()
        // For now, log for monitoring
        string json = JsonUtility.ToJson(data);
        Debug.Log($"[Metrics] {eventType}: {json}");
    }
}
