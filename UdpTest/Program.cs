using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace UdpTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("UDP Client Test - Testing connection to localhost:8081");
            
            try
            {
                using var client = new UdpClient();
                var serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8081);
                
                // Send join message
                var message = @"{""op"":""join"",""name"":""TestPlayer""}";
                var data = Encoding.UTF8.GetBytes(message);
                
                Console.WriteLine($"Sending: {message}");
                await client.SendAsync(data, data.Length, serverEndpoint);
                Console.WriteLine("Message sent successfully!");
                
                // Try to receive response
                Console.WriteLine("Waiting for response...");
                var timeout = Task.Delay(5000); // 5 second timeout
                var receive = client.ReceiveAsync();
                
                var completed = await Task.WhenAny(receive, timeout);
                
                if (completed == receive)
                {
                    var result = await receive;
                    var response = Encoding.UTF8.GetString(result.Buffer);
                    Console.WriteLine($"Received: {response}");
                }
                else
                {
                    Console.WriteLine("Timeout - no response received");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
