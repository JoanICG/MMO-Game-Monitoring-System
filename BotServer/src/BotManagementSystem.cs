using System.Collections.Concurrent;
using Shared.Models;

namespace BotServer
{
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

        public Bot()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
            LastUpdate = DateTime.UtcNow;
            IsActive = true;
            Behavior = BotBehavior.Random;
        }

        public void UpdatePosition()
        {
            if (!IsActive) return;

            switch (Behavior)
            {
                case BotBehavior.Random:
                    UpdateRandomMovement();
                    break;
                case BotBehavior.Patrol:
                    UpdatePatrolMovement();
                    break;
                case BotBehavior.Follow:
                    // Follow behavior would need target information
                    UpdateRandomMovement(); // Fallback to random
                    break;
                case BotBehavior.Idle:
                default:
                    // Don't move
                    break;
            }

            LastUpdate = DateTime.UtcNow;
        }

        private void UpdateRandomMovement()
        {
            const float speed = 3.0f;
            const float deltaTime = 0.1f;
            const float mapSize = 50.0f;

            var angle = Random.Shared.NextSingle() * 2 * Math.PI;
            var moveX = (float)Math.Cos(angle) * speed * deltaTime;
            var moveZ = (float)Math.Sin(angle) * speed * deltaTime;

            X = Math.Clamp(X + moveX, -mapSize, mapSize);
            Z = Math.Clamp(Z + moveZ, -mapSize, mapSize);
        }

        private void UpdatePatrolMovement()
        {
            const float speed = 2.0f;
            const float deltaTime = 0.1f;
            const float patrolSize = 10.0f;

            // Simple patrol - move in a square
            var time = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond) % 40;
            var phase = time / 10.0f;

            float targetX = 0, targetZ = 0;
            switch ((int)phase)
            {
                case 0: targetX = patrolSize; targetZ = 0; break;
                case 1: targetX = patrolSize; targetZ = patrolSize; break;
                case 2: targetX = 0; targetZ = patrolSize; break;
                default: targetX = 0; targetZ = 0; break;
            }

            var dirX = targetX - X;
            var dirZ = targetZ - Z;
            var distance = Math.Sqrt(dirX * dirX + dirZ * dirZ);

            if (distance > 0.1f)
            {
                dirX /= (float)distance;
                dirZ /= (float)distance;
                X += dirX * speed * deltaTime;
                Z += dirZ * speed * deltaTime;
            }
        }

        public BotDto ToDto()
        {
            return new BotDto
            {
                Id = Id,
                Name = Name,
                X = X,
                Y = Y,
                Z = Z,
                IsActive = IsActive,
                Behavior = Behavior
            };
        }
    }

    public class BotContainer : IDisposable
    {
        public Guid Id { get; }
        public string Name { get; }
        public int MaxBots { get; }
        public DateTime CreatedAt { get; }
        public bool IsActive { get; private set; }

        private readonly ConcurrentDictionary<Guid, Bot> _bots = new();
        private readonly Timer _updateTimer;

        public IReadOnlyDictionary<Guid, Bot> Bots => _bots;
        public int ActiveBotsCount => _bots.Values.Count(b => b.IsActive);

        public BotContainer(string name, int maxBots = 50)
        {
            Id = Guid.NewGuid();
            Name = name;
            MaxBots = maxBots;
            CreatedAt = DateTime.UtcNow;
            IsActive = true;

            // Update bots every 100ms
            _updateTimer = new Timer(UpdateBots, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        }

        public bool AddBot(string name)
        {
            if (_bots.Count >= MaxBots || !IsActive) return false;

            var bot = new Bot
            {
                Name = name,
                ContainerId = Id,
                X = Random.Shared.NextSingle() * 20 - 10, // Random position -10 to 10
                Z = Random.Shared.NextSingle() * 20 - 10,
                Y = 0
            };

            return _bots.TryAdd(bot.Id, bot);
        }

        public bool RemoveBot(Guid botId)
        {
            return _bots.TryRemove(botId, out _);
        }

        public void RemoveAllBots()
        {
            _bots.Clear();
        }

        public void PauseAllBots()
        {
            foreach (var bot in _bots.Values)
            {
                bot.IsActive = false;
            }
        }

        public void ResumeAllBots()
        {
            foreach (var bot in _bots.Values)
            {
                bot.IsActive = true;
            }
        }

        public List<Bot> GetAllBots()
        {
            return _bots.Values.ToList();
        }

        public List<Bot> GetActiveBots()
        {
            return _bots.Values.Where(b => b.IsActive).ToList();
        }

        public Bot? GetBot(Guid botId)
        {
            _bots.TryGetValue(botId, out var bot);
            return bot;
        }

        public BotContainerStats GetStats()
        {
            return new BotContainerStats
            {
                ContainerId = Id,
                ContainerName = Name,
                MaxBots = MaxBots,
                TotalBots = _bots.Count,
                ActiveBots = ActiveBotsCount,
                PausedBots = _bots.Count - ActiveBotsCount,
                IsActive = IsActive,
                CreatedAt = CreatedAt
            };
        }

        private void UpdateBots(object? state)
        {
            if (!IsActive) return;

            foreach (var bot in _bots.Values)
            {
                bot.UpdatePosition();
            }
        }

        public void Dispose()
        {
            IsActive = false;
            _updateTimer?.Dispose();
            _bots.Clear();
        }
    }

    public class BotManagementSystem
    {
        private static BotManagementSystem? _instance;
        public static BotManagementSystem Instance => _instance ??= new BotManagementSystem();
        
        private readonly ConcurrentDictionary<Guid, BotContainer> _containers = new();
        private readonly object _lockObject = new();
        
        public IReadOnlyDictionary<Guid, BotContainer> Containers => _containers;
        public int TotalContainers => _containers.Count;
        public int TotalBots => _containers.Values.Sum(c => c.Bots.Count);
        public int TotalActiveBots => _containers.Values.Sum(c => c.ActiveBotsCount);
        
        private BotManagementSystem()
        {
            Console.WriteLine("[BotServer] Bot Management System initialized");
        }
        
        public BotContainer? CreateContainer(string name, int maxBots = 50)
        {
            lock (_lockObject)
            {
                var container = new BotContainer(name, maxBots);
                
                if (_containers.TryAdd(container.Id, container))
                {
                    Console.WriteLine($"[BotServer] Created container '{name}' with ID {container.Id}");
                    return container;
                }
                
                container.Dispose();
                return null;
            }
        }
        
        public bool RemoveContainer(Guid containerId)
        {
            lock (_lockObject)
            {
                if (_containers.TryRemove(containerId, out var container))
                {
                    container.Dispose();
                    Console.WriteLine($"[BotServer] Removed container '{container.Name}'");
                    return true;
                }
                return false;
            }
        }
        
        public BotContainer? GetContainer(Guid containerId)
        {
            _containers.TryGetValue(containerId, out var container);
            return container;
        }
        
        public List<BotContainer> GetAllContainers()
        {
            return _containers.Values.ToList();
        }
        
        public void PauseAllBots()
        {
            foreach (var container in _containers.Values)
            {
                container.PauseAllBots();
            }
        }
        
        public void ResumeAllBots()
        {
            foreach (var container in _containers.Values)
            {
                container.ResumeAllBots();
            }
        }
        
        public void RemoveAllBots()
        {
            foreach (var container in _containers.Values)
            {
                container.RemoveAllBots();
            }
        }
        
        public void RemoveAllContainers()
        {
            lock (_lockObject)
            {
                foreach (var container in _containers.Values)
                {
                    container.Dispose();
                }
                _containers.Clear();
            }
        }
        
        public List<Bot> GetAllBots()
        {
            var allBots = new List<Bot>();
            foreach (var container in _containers.Values)
            {
                allBots.AddRange(container.GetAllBots());
            }
            return allBots;
        }
        
        public BotSystemStats GetSystemStats()
        {
            var containers = _containers.Values.ToList();
            
            return new BotSystemStats
            {
                TotalContainers = containers.Count,
                ActiveContainers = containers.Count(c => c.IsActive),
                TotalBots = containers.Sum(c => c.Bots.Count),
                ActiveBots = containers.Sum(c => c.ActiveBotsCount),
                PausedBots = containers.Sum(c => c.Bots.Count(kvp => !kvp.Value.IsActive)),
                ContainerStats = containers.Select(c => c.GetStats()).ToList()
            };
        }
    }
}
