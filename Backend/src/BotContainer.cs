using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Backend
{
    public class BotContainer
    {
        private static readonly Random _random = new Random();
        
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public int MaxBots { get; private set; }
        public bool IsActive { get; private set; }
        public DateTime CreatedAt { get; private set; }
        
        private readonly ConcurrentDictionary<Guid, Bot> _bots;
        private readonly Timer _updateTimer;
        private readonly object _lockObject = new object();
        
        public IReadOnlyDictionary<Guid, Bot> Bots => _bots;
        public int ActiveBotsCount => _bots.Count(kvp => kvp.Value.IsActive);
        
        public BotContainer(string name, int maxBots = 50)
        {
            Id = Guid.NewGuid();
            Name = name;
            MaxBots = maxBots;
            IsActive = true;
            CreatedAt = DateTime.UtcNow;
            _bots = new ConcurrentDictionary<Guid, Bot>();
            
            // Update bots every 100ms
            _updateTimer = new Timer(UpdateBots, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            
            Console.WriteLine($"[BotContainer] Created container '{name}' with max {maxBots} bots");
        }
        
        public bool AddBot(string botName = null)
        {
            lock (_lockObject)
            {
                if (!IsActive || _bots.Count >= MaxBots)
                {
                    Console.WriteLine($"[BotContainer] Cannot add bot: Container full or inactive");
                    return false;
                }
                
                var name = botName ?? $"Bot_{_bots.Count + 1}";
                
                var bot = new Bot
                {
                    Name = name,
                    X = (float)(_random.NextDouble() * 50 - 25), // -25 to 25
                    Y = 0,
                    Z = (float)(_random.NextDouble() * 50 - 25), // -25 to 25
                    Behavior = BotBehavior.Random,
                    Speed = (float)(_random.NextDouble() * 4.0 + 2.0), // 2-6 speed (faster)
                    IsActive = true
                };
                
                if (_bots.TryAdd(bot.Id, bot))
                {
                    Console.WriteLine($"[BotContainer] Added bot '{bot.Name}' at position ({bot.X}, {bot.Y}, {bot.Z})");
                    return true;
                }
                
                return false;
            }
        }
        
        public bool RemoveBot(Guid botId)
        {
            lock (_lockObject)
            {
                if (_bots.TryRemove(botId, out var bot))
                {
                    bot.IsActive = false;
                    Console.WriteLine($"[BotContainer] Removed bot '{bot.Name}'");
                    return true;
                }
                return false;
            }
        }
        
        public void RemoveAllBots()
        {
            lock (_lockObject)
            {
                foreach (var bot in _bots.Values)
                {
                    bot.IsActive = false;
                }
                _bots.Clear();
                Console.WriteLine($"[BotContainer] Removed all bots from container '{Name}'");
            }
        }
        
        public bool PauseBot(Guid botId)
        {
            if (_bots.TryGetValue(botId, out var bot))
            {
                bot.IsActive = false;
                Console.WriteLine($"[BotContainer] Paused bot '{bot.Name}'");
                return true;
            }
            return false;
        }
        
        public bool ResumeBot(Guid botId)
        {
            if (_bots.TryGetValue(botId, out var bot))
            {
                bot.IsActive = true;
                Console.WriteLine($"[BotContainer] Resumed bot '{bot.Name}'");
                return true;
            }
            return false;
        }
        
        public void PauseAllBots()
        {
            foreach (var bot in _bots.Values)
            {
                bot.IsActive = false;
            }
            Console.WriteLine($"[BotContainer] Paused all bots in container '{Name}'");
        }
        
        public void ResumeAllBots()
        {
            foreach (var bot in _bots.Values)
            {
                bot.IsActive = true;
            }
            Console.WriteLine($"[BotContainer] Resumed all bots in container '{Name}'");
        }
        
        public Bot GetBot(Guid botId)
        {
            _bots.TryGetValue(botId, out var bot);
            return bot;
        }
        
        public List<Bot> GetAllBots()
        {
            return _bots.Values.ToList();
        }
        
        public List<Bot> GetActiveBots()
        {
            return _bots.Values.Where(b => b.IsActive).ToList();
        }
        
        private void UpdateBots(object state)
        {
            if (!IsActive) return;
            
            const float deltaTime = 0.1f; // 100ms
            
            var activeBots = _bots.Values.Where(b => b.IsActive).ToList();
            
            Parallel.ForEach(activeBots, bot =>
            {
                try
                {
                    bot.Update(deltaTime);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BotContainer] Error updating bot {bot.Name}: {ex.Message}");
                }
            });
        }
        
        public void Dispose()
        {
            IsActive = false;
            _updateTimer?.Dispose();
            RemoveAllBots();
            Console.WriteLine($"[BotContainer] Disposed container '{Name}'");
        }
        
        public BotContainerStats GetStats()
        {
            return new BotContainerStats
            {
                ContainerId = Id,
                ContainerName = Name,
                TotalBots = _bots.Count,
                ActiveBots = _bots.Count(kvp => kvp.Value.IsActive),
                PausedBots = _bots.Count(kvp => !kvp.Value.IsActive),
                MaxBots = MaxBots,
                IsActive = IsActive,
                CreatedAt = CreatedAt
            };
        }
    }
    
    public class BotContainerStats
    {
        public Guid ContainerId { get; set; }
        public string ContainerName { get; set; }
        public int TotalBots { get; set; }
        public int ActiveBots { get; set; }
        public int PausedBots { get; set; }
        public int MaxBots { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
