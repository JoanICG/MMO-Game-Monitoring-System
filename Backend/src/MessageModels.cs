namespace Backend;

// Incoming
public record JoinRequest(string Op, string Name);
public record MoveMessage(string Op, float X, float Y, float Z);

// Admin messages
public record AdminJoinRequest(string Op, string Name);
public record AdminSpawnNPC(string Op, string Name, float X, float Y, float Z);
public record AdminTeleport(string Op, string PlayerId, float X, float Y, float Z);
public record AdminKick(string Op, string PlayerId);
public record AdminTeleportAll(string Op, float X, float Y, float Z);
public record AdminKickAll(string Op);

// Outgoing
public record JoinAck(string Op, Guid Id);
public record PlayerDto(Guid Id, string Name, float X, float Y, float Z, bool IsNPC = false);
public record StateSnapshot(string Op, IEnumerable<PlayerDto> Players, ServerBenchmarkMetrics? BenchmarkMetrics = null);
