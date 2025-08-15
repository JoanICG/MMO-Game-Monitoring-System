namespace Backend;

public class PlayerState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public bool IsAdmin { get; set; } = false;
    public bool IsNPC { get; set; } = false;
    public DateTime? ServerAuthorityUntil { get; set; } = null; // Server authority protection
    
    // Bot AI properties
    public bool IsBot { get; set; } = false;
    public BotBehavior BotBehavior { get; set; } = BotBehavior.Idle;
    public Guid? FollowTargetId { get; set; } = null;
    public float BotSpeed { get; set; } = 3f;
    public DateTime LastBotUpdate { get; set; } = DateTime.UtcNow;
    public Vector3? BotMoveDirection { get; set; } = null; // Current movement direction for random movement
    
    // Bot waypoints for patrol
    public List<Vector3> PatrolWaypoints { get; set; } = new();
    public int CurrentWaypointIndex { get; set; } = 0;
}

public enum BotBehavior
{
    Idle,
    Patrol,
    Follow,
    MoveTo,
    Random
}

public struct Vector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    
    public Vector3(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }
    
    public float Distance(Vector3 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    
    public Vector3 Normalized()
    {
        var magnitude = (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        if (magnitude > 0.001f)
            return new Vector3(X / magnitude, Y / magnitude, Z / magnitude);
        return new Vector3(0, 0, 0);
    }
}
