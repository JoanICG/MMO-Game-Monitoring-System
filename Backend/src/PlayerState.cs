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
}
