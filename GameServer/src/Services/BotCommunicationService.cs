using System.Text.Json;
using Shared.Models;

namespace GameServer.Services
{
    public interface IBotCommunicationService
    {
        Task<List<BotDto>> GetBotsFromBotServer();
        Task SendCommandToBotServer(string command, object? parameters = null);
        event Action<List<BotDto>>? BotsUpdated;
    }

    public class BotCommunicationService : IBotCommunicationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _botServerUrl;
        private readonly Timer _pollTimer;
        private List<BotDto> _lastKnownBots = new();
        
        public event Action<List<BotDto>>? BotsUpdated;

        public BotCommunicationService(string botServerUrl = "http://bot-server:8082")
        {
            _botServerUrl = botServerUrl;
            _httpClient = new HttpClient();
            
            // Poll bot server every 100ms for updates
            _pollTimer = new Timer(PollBotServer, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            
            Console.WriteLine($"[BotCommunication] Bot Communication Service initialized, connecting to {_botServerUrl}");
        }

        public async Task<List<BotDto>> GetBotsFromBotServer()
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"{_botServerUrl}/api/bots");
                var bots = JsonSerializer.Deserialize<List<BotDto>>(response, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                }) ?? new List<BotDto>();
                
                return bots;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotCommunication] Error getting bots from bot server: {ex.Message}");
                return new List<BotDto>();
            }
        }

        public async Task SendCommandToBotServer(string command, object? parameters = null)
        {
            try
            {
                var request = new BotCommandRequest(command, null, null, parameters);
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_botServerUrl}/api/commands", content);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[BotCommunication] Command '{command}' sent successfully to bot server");
                }
                else
                {
                    Console.WriteLine($"[BotCommunication] Failed to send command '{command}': {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotCommunication] Error sending command to bot server: {ex.Message}");
            }
        }

        private async void PollBotServer(object? state)
        {
            try
            {
                var currentBots = await GetBotsFromBotServer();
                
                // Check if bots have changed
                if (!AreBotsEqual(_lastKnownBots, currentBots))
                {
                    _lastKnownBots = currentBots;
                    BotsUpdated?.Invoke(currentBots);
                }
            }
            catch (Exception ex)
            {
                // Silently handle polling errors to avoid spam
                if (DateTime.UtcNow.Second % 30 == 0) // Log every 30 seconds
                {
                    Console.WriteLine($"[BotCommunication] Polling error: {ex.Message}");
                }
            }
        }

        private static bool AreBotsEqual(List<BotDto> list1, List<BotDto> list2)
        {
            if (list1.Count != list2.Count) return false;
            
            // Simple comparison - could be optimized for better performance
            var serialized1 = JsonSerializer.Serialize(list1.OrderBy(b => b.Id));
            var serialized2 = JsonSerializer.Serialize(list2.OrderBy(b => b.Id));
            
            return serialized1 == serialized2;
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
            _httpClient?.Dispose();
            Console.WriteLine("[BotCommunication] Bot Communication Service disposed");
        }
    }
}
