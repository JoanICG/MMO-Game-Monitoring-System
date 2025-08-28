using System.Text.Json.Serialization;

namespace Shared.Models
{
    // Player and Game State Models
    public class PlayerState
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public DateTime LastUpdate { get; set; }
        public bool IsBot { get; set; }
        public bool IsNPC { get; set; }
        public bool IsAdmin { get; set; }
        public BotBehavior BotBehavior { get; set; } = BotBehavior.Idle;
        public List<Vector3> PatrolWaypoints { get; set; } = new();
    }

    public class PlayerSession
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastInputTime { get; set; }
        public System.Net.EndPoint EndPoint { get; set; }
        public bool IsConnected { get; set; }
    }

    public class Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Vector3() { }
        public Vector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }

    // Advanced Bot Behaviors
    public enum BotBehavior
    {
        Idle,
        Random,
        Follow,
        Patrol,
        // New intelligent behaviors
        Social,           // Interact with other bots/players
        Aggressive,       // Attack nearby players
        Defensive,        // Stay near allies, avoid threats
        Explorer,         // Explore the map systematically
        Hunter,           // Hunt specific targets
        Guardian,         // Protect specific areas/players
        Trader,           // Engage in trading activities
        Quest,            // Follow quest-like behaviors
        Formation,        // Move in formation with other bots
        Ambush,           // Set up ambushes
        Scout,            // Gather information about player positions
        Evade,            // Run away from threats
        Assist,           // Help other bots/players
        Competitive       // Compete with players for objectives
    }

    // Bot Perception and Awareness
    public class BotPerception
    {
        public Guid BotId { get; set; }
        public List<NearbyEntity> NearbyPlayers { get; set; } = new();
        public List<NearbyEntity> NearbyBots { get; set; } = new();
        public List<Threat> Threats { get; set; } = new();
        public List<Opportunity> Opportunities { get; set; } = new();
        public GameStateSummary GameState { get; set; } = new();
        public DateTime LastUpdate { get; set; }
    }

    public class NearbyEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Distance { get; set; }
        public bool IsPlayer { get; set; }
        public BotBehavior Behavior { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class Threat
    {
        public Guid SourceId { get; set; }
        public ThreatLevel Level { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    public enum ThreatLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class Opportunity
    {
        public string Type { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public float Value { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class GameStateSummary
    {
        public int TotalPlayers { get; set; }
        public int TotalBots { get; set; }
        public Dictionary<string, int> BehaviorDistribution { get; set; } = new();
        public List<Hotspot> Hotspots { get; set; } = new();
        public float AveragePlayerLevel { get; set; }
        public TimeSpan GameDuration { get; set; }
    }

    public class Hotspot
    {
        public Vector3 Center { get; set; }
        public float Radius { get; set; }
        public int PlayerCount { get; set; }
        public int BotCount { get; set; }
        public string Activity { get; set; } = string.Empty;
    }

    // Bot Decision Making
    public class BotDecision
    {
        public Guid BotId { get; set; }
        public BotAction Action { get; set; }
        public Vector3 TargetPosition { get; set; }
        public Guid? TargetEntityId { get; set; }
        public float Priority { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime MadeAt { get; set; }
    }

    public enum BotAction
    {
        Move,
        Follow,
        Attack,
        Defend,
        Flee,
        Socialize,
        Explore,
        Guard,
        Patrol,
        Ambush,
        Communicate,
        Trade,
        Idle
    }

    // Bot Memory and Learning
    public class BotMemory
    {
        public Guid BotId { get; set; }
        public Dictionary<string, object> Facts { get; set; } = new();
        public List<BotExperience> Experiences { get; set; } = new();
        public Dictionary<Guid, Relationship> Relationships { get; set; } = new();
        public List<Vector3> KnownLocations { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }

    public class BotExperience
    {
        public string Situation { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public float Reward { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public class Relationship
    {
        public Guid EntityId { get; set; }
        public float Trust { get; set; } // -1 to 1
        public float Aggression { get; set; } // -1 to 1
        public int Interactions { get; set; }
        public DateTime LastInteraction { get; set; }
    }

    // Communication System
    public class BotMessage
    {
        public Guid FromBotId { get; set; }
        public Guid? ToBotId { get; set; } // null = broadcast
        public string Type { get; set; } = string.Empty;
        public object Data { get; set; }
        public DateTime SentAt { get; set; }
        public Vector3 Position { get; set; }
    }

    // Bot Configuration
    public class BotConfiguration
    {
        public Guid BotId { get; set; }
        public string Personality { get; set; } = "Balanced";
        public Dictionary<BotBehavior, float> BehaviorWeights { get; set; } = new();
        public float Aggressiveness { get; set; } = 0.5f;
        public float Socialness { get; set; } = 0.5f;
        public float Curiosity { get; set; } = 0.5f;
        public float RiskTolerance { get; set; } = 0.5f;
        public List<string> PreferredBehaviors { get; set; } = new();
    }

    // Bot Models
    public class Bot
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public bool IsActive { get; set; }
        public BotBehavior Behavior { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdate { get; set; }
        public Guid ContainerId { get; set; }
    }

    // Network Messages
    public class GameStateUpdate
    {
        public string Type { get; set; } = "state_update";
        public List<PlayerDto> Players { get; set; } = new();
        public List<BotDto> Bots { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public ServerMetrics Metrics { get; set; } = new();
    }

    public class PlayerDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public bool IsNPC { get; set; }

        public PlayerDto() { }
        public PlayerDto(Guid id, string name, float x, float y, float z, bool isNpc)
        {
            Id = id; Name = name; X = x; Y = y; Z = z; IsNPC = isNpc;
        }
    }

    public class BotDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public bool IsActive { get; set; }
        public BotBehavior Behavior { get; set; }
    }

    public class StateSnapshot
    {
        public string Op { get; set; }
        public List<PlayerDto> Players { get; set; }
        public ServerBenchmarkMetrics Metrics { get; set; }

        public StateSnapshot(string op, List<PlayerDto> players, ServerBenchmarkMetrics metrics)
        {
            Op = op;
            Players = players;
            Metrics = metrics;
        }
    }

    public class ServerBenchmarkMetrics
    {
        public int TotalEntities { get; set; }
        public int RealPlayers { get; set; }
        public int TotalBots { get; set; }
        public int ActiveBots { get; set; }
        public double UpdatesPerSecond { get; set; }
        public long MemoryUsageMB { get; set; }
        public double UptimeSeconds { get; set; }
    }

    public class ServerMetrics
    {
        public int PlayerCount { get; set; }
        public int BotCount { get; set; }
        public int ActiveConnections { get; set; }
        public double MemoryUsageMB { get; set; }
        public double CpuUsagePercent { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Bot Management Models
    public class BotContainerInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxBots { get; set; }
        public int CurrentBots { get; set; }
        public int ActiveBots { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class BotSystemStats
    {
        public int TotalContainers { get; set; }
        public int ActiveContainers { get; set; }
        public int TotalBots { get; set; }
        public int ActiveBots { get; set; }
        public int PausedBots { get; set; }
        public List<BotContainerStats> ContainerStats { get; set; } = new();
    }

    public class BotContainerStats
    {
        public Guid ContainerId { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public int MaxBots { get; set; }
        public int TotalBots { get; set; }
        public int ActiveBots { get; set; }
        public int PausedBots { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // API Request/Response Models
    public record CreateContainerRequest(string Name, int MaxBots = 50);
    public record SpawnBotsRequest(int Count);
    public record BotCommandRequest(string Command, Guid? ContainerId = null, Guid? BotId = null, object? Parameters = null);

    // Communication Messages between services
    public class ServiceMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public object Data { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid MessageId { get; set; } = Guid.NewGuid();
    }

    public class BotUpdateMessage
    {
        public string Type { get; set; } = "bot_update";
        public List<BotDto> Bots { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}
