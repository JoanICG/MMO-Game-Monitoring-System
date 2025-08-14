using System.Threading.Tasks;
using UnityEngine;

// Attach this to a Player GameObject with a CharacterController or simply a Transform.
public class LocalPlayerController : MonoBehaviour
{
    public float speed = 5f;
    public float sendRate = 10f; // Max 10 messages per second
    
    private NetworkClient _net;
    private float _lastSendTime;
    private Vector3 _lastSentPosition;
    private const float MIN_MOVE_DISTANCE = 0.01f; // Only send if moved enough
    
    // Server authority protection
    private float _serverAuthorityTime;
    private const float SERVER_AUTHORITY_COOLDOWN = 2.0f; // 2 seconds cooldown after server teleport
    private bool _serverAuthorityActive = false; // Flag to track if we're in server authority mode

    /// <summary>
    /// Called by NetworkClient when server has authority (teleport, admin commands)
    /// </summary>
    public void SetServerPosition(Vector3 serverPos)
    {
        Debug.LogWarning($"[LocalPlayerController] *** SERVER AUTHORITY ACTIVATED *** position set to {serverPos}, transform was {transform.position}");
        
        // Force immediate position sync to prevent drift
        if (Vector3.Distance(transform.position, serverPos) > 0.1f)
        {
            Debug.LogWarning($"[LocalPlayerController] Large position diff detected, forcing sync from {transform.position} to {serverPos}");
        }
        
        // CRITICAL: Actually update the transform position to match server
        transform.position = serverPos;
        
        // Update our tracking to match server position
        _lastSentPosition = serverPos;
        _serverAuthorityTime = Time.time; // Mark when server took authority
        _serverAuthorityActive = true; // Set the flag
        
        // Also reset the last send time to prevent immediate send after cooldown
        _lastSendTime = Time.time;
        
        Debug.LogWarning($"[LocalPlayerController] Transform position updated to {transform.position}, cooldown active for {SERVER_AUTHORITY_COOLDOWN}s, authority flag set");
    }

    private void Start()
    {
        _net = NetworkClient.Instance;
        _lastSentPosition = transform.position;
    }

    private async void Update()
    {
        if (_net == null || _net.localPlayerId == System.Guid.Empty) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (h != 0 || v != 0)
        {
            var delta = new Vector3(h, 0, v).normalized * speed * Time.deltaTime;
            transform.position += delta;
            
            // Check if we're in server authority cooldown period
            bool inCooldown = (Time.time - _serverAuthorityTime < SERVER_AUTHORITY_COOLDOWN) && _serverAuthorityActive;
            if (inCooldown)
            {
                float remainingTime = SERVER_AUTHORITY_COOLDOWN - (Time.time - _serverAuthorityTime);
                Debug.LogWarning($"[LocalPlayerController] *** BLOCKING MOVE *** Server authority cooldown active ({remainingTime:F2}s remaining)");
                return;
            }
            
            // Clear the authority flag once cooldown expires
            if (_serverAuthorityActive && Time.time - _serverAuthorityTime >= SERVER_AUTHORITY_COOLDOWN)
            {
                _serverAuthorityActive = false;
                Debug.Log($"[LocalPlayerController] Server authority cooldown expired, resuming normal movement");
            }
            
            // Throttle network messages
            if (Time.time - _lastSendTime >= 1f / sendRate)
            {
                var distance = Vector3.Distance(transform.position, _lastSentPosition);
                if (distance >= MIN_MOVE_DISTANCE)
                {
                    Debug.Log($"[LocalPlayerController] Sending move: {transform.position} (last sent: {_lastSentPosition}, distance: {distance:F3})");
                    await SendMove(transform.position);
                    _lastSendTime = Time.time;
                    _lastSentPosition = transform.position;
                }
            }
        }
    }

    private Task SendMove(Vector3 pos)
    {
        return _net.SendMove(pos);
    }
}
