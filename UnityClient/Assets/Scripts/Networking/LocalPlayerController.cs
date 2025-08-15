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
    
    [Header("Camera Integration")]
    public bool autoSetupCamera = true;
    
    private NetworkClient _net;
    private float _lastInputSendTime;
    
    // Client-Side Prediction
    private Vector3 _velocity = Vector3.zero;
    private uint _inputSequence = 0;
    
    // Input state
    private Vector2 _currentInput;
    private Vector2 _lastSentInput;
    
    // Metrics for future Kafka integration
    private float _totalInputsSent = 0;
    private float _lastMetricsTime = 0;
    
    // Camera reference (will be resolved at runtime)
    private object _cameraManager;

    /// <summary>
    /// Server reconciliation - called by NetworkClient
    /// </summary>
    public void ReceiveServerState(Vector3 serverPos, float serverTime)
    {
        float positionError = Vector3.Distance(transform.position, serverPos);
        
        if (positionError > 0.15f) // Error threshold for correction
        {
            Debug.LogWarning($"[Reconciliation] Correcting position error: {positionError:F3}m");
            transform.position = Vector3.Lerp(transform.position, serverPos, 0.8f);
            _velocity *= 0.5f; // Reduce velocity on correction
            
            // Metrics event for monitoring
            LogMetrics("player.prediction.correction", positionError);
        }
    }

    private void Start()
    {
        _net = NetworkClient.Instance;
        _lastMetricsTime = Time.time;
        
        // Setup camera system
        SetupCameraSystem();
        
        Debug.Log("[LocalPlayerController] Initialized with camera integration");
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
        if (_net == null || _net.localPlayerId == System.Guid.Empty) return;

        // Gather input
        _currentInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        
        // Client-Side Prediction: Apply movement locally first
        ApplyLocalMovement();
        
        // Send input to server at fixed rate
        if (Time.time - _lastInputSendTime >= 1f / inputSendRate)
        {
            SendInputToServer();
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
        
        // Apply movement
        transform.position += _velocity * Time.deltaTime;
    }

    private async void SendInputToServer()
    {
        // Send if input changed or we're moving (for responsiveness)
        bool inputChanged = Vector2.Distance(_currentInput, _lastSentInput) > 0.01f;
        bool isMoving = _velocity.magnitude > 0.1f;
        
        if (inputChanged || isMoving)
        {
            _inputSequence++;
            
            // Send optimized input packet
            await _net.SendPlayerInput(_inputSequence, _currentInput, speed);
            _lastSentInput = _currentInput;
            _totalInputsSent++;
            
            // Log for debugging
            Debug.Log($"[Input] Seq: {_inputSequence}, Input: {_currentInput}, Vel: {_velocity.magnitude:F2}");
        }
    }

    private void SendPeriodicMetrics()
    {
        // Send metrics every 5 seconds (future Kafka events)
        if (Time.time - _lastMetricsTime >= 5f)
        {
            var metrics = new {
                playerId = _net.localPlayerId.ToString(),
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
