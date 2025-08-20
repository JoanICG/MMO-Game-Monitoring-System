using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Backend
{
    public class BotManagementSystem
    {
        private static BotManagementSystem _instance;
        public static BotManagementSystem Instance => _instance ??= new BotManagementSystem();
        
        private readonly ConcurrentDictionary<Guid, BotContainer> _containers;
        private readonly object _lockObject = new object();
        
        public IReadOnlyDictionary<Guid, BotContainer> Containers => _containers;
        public int TotalContainers => _containers.Count;
        public int TotalBots => _containers.Values.Sum(c => c.Bots.Count);
        public int TotalActiveBots => _containers.Values.Sum(c => c.ActiveBotsCount);
        
        private BotManagementSystem()
        {
            _containers = new ConcurrentDictionary<Guid, BotContainer>();
            Console.WriteLine("[BotManagement] Bot Management System initialized");
        }
        
        // Container Management
        public BotContainer CreateContainer(string name, int maxBots = 50)
        {
            lock (_lockObject)
            {
                var container = new BotContainer(name, maxBots);
                
                if (_containers.TryAdd(container.Id, container))
                {
                    Console.WriteLine($"[BotManagement] Created container '{name}' with ID {container.Id}");
                    return container;
                }
                
                container.Dispose();
                throw new InvalidOperationException("Failed to create container");
            }
        }
        
        public bool RemoveContainer(Guid containerId)
        {
            lock (_lockObject)
            {
                if (_containers.TryRemove(containerId, out var container))
                {
                    container.Dispose();
                    Console.WriteLine($"[BotManagement] Removed container '{container.Name}'");
                    return true;
                }
                return false;
            }
        }
        
        public BotContainer GetContainer(Guid containerId)
        {
            _containers.TryGetValue(containerId, out var container);
            return container;
        }
        
        public List<BotContainer> GetAllContainers()
        {
            return _containers.Values.ToList();
        }
        
        public List<BotContainer> GetActiveContainers()
        {
            return _containers.Values.Where(c => c.IsActive).ToList();
        }
        
        // Bulk Bot Operations
        public int CreateBotsInContainer(Guid containerId, int count, string namePrefix = "Bot")
        {
            var container = GetContainer(containerId);
            if (container == null) return 0;
            
            int created = 0;
            for (int i = 0; i < count; i++)
            {
                var botName = $"{namePrefix}_{container.Bots.Count + 1}";
                if (container.AddBot(botName))
                {
                    created++;
                }
                else
                {
                    break; // Container full
                }
            }
            
            Console.WriteLine($"[BotManagement] Created {created} bots in container '{container.Name}'");
            return created;
        }
        
        public void PauseAllBotsInContainer(Guid containerId)
        {
            var container = GetContainer(containerId);
            container?.PauseAllBots();
        }
        
        public void ResumeAllBotsInContainer(Guid containerId)
        {
            var container = GetContainer(containerId);
            container?.ResumeAllBots();
        }
        
        public void RemoveAllBotsInContainer(Guid containerId)
        {
            var container = GetContainer(containerId);
            container?.RemoveAllBots();
        }
        
        // Global Operations
        public void PauseAllBots()
        {
            foreach (var container in _containers.Values)
            {
                container.PauseAllBots();
            }
            Console.WriteLine("[BotManagement] Paused all bots globally");
        }
        
        public void ResumeAllBots()
        {
            foreach (var container in _containers.Values)
            {
                container.ResumeAllBots();
            }
            Console.WriteLine("[BotManagement] Resumed all bots globally");
        }
        
        public void RemoveAllBots()
        {
            foreach (var container in _containers.Values)
            {
                container.RemoveAllBots();
            }
            Console.WriteLine("[BotManagement] Removed all bots globally");
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
                Console.WriteLine("[BotManagement] Removed all containers");
            }
        }
        
        // Statistics and Monitoring
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
        
        public List<Bot> GetAllBots()
        {
            var allBots = new List<Bot>();
            foreach (var container in _containers.Values)
            {
                allBots.AddRange(container.GetAllBots());
            }
            return allBots;
        }
        
        public List<Bot> GetAllActiveBots()
        {
            var allBots = new List<Bot>();
            foreach (var container in _containers.Values)
            {
                allBots.AddRange(container.GetActiveBots());
            }
            return allBots;
        }
        
        public Bot FindBot(Guid botId)
        {
            foreach (var container in _containers.Values)
            {
                var bot = container.GetBot(botId);
                if (bot != null) return bot;
            }
            return null;
        }
        
        // Quick Setup Methods
        public BotContainer CreateDefaultContainer()
        {
            return CreateContainer($"DefaultContainer_{DateTime.Now:HHmmss}", 25);
        }
        
        public void CreateTestEnvironment()
        {
            var container1 = CreateContainer("TestBots_Small", 10);
            var container2 = CreateContainer("TestBots_Medium", 25);
            
            CreateBotsInContainer(container1.Id, 5, "SmallBot");
            CreateBotsInContainer(container2.Id, 15, "MediumBot");
            
            Console.WriteLine("[BotManagement] Test environment created");
        }
        
        public void Dispose()
        {
            RemoveAllContainers();
            Console.WriteLine("[BotManagement] Bot Management System disposed");
        }
    }
    
    public class BotSystemStats
    {
        public int TotalContainers { get; set; }
        public int ActiveContainers { get; set; }
        public int TotalBots { get; set; }
        public int ActiveBots { get; set; }
        public int PausedBots { get; set; }
        public List<BotContainerStats> ContainerStats { get; set; } = new List<BotContainerStats>();
    }
}
