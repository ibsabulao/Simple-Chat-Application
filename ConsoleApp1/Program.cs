using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Supabase;
using Supabase.Interfaces;
using Supabase.Postgrest.Models;
using Supabase.Realtime;
using static Supabase.Postgrest.Constants;

namespace ChatAppServer
{
    class Program
    {
        private static readonly List<TcpClient> _clients = new List<TcpClient>();
        private static readonly Dictionary<string, TcpClient> _clientUsernames = new Dictionary<string, TcpClient>();

        private static Supabase.Client? _supabaseClient;
        private static readonly string SupabaseUrl = "https://xixzivkrwqzxdizitthv.supabase.co";
        private static readonly string SupabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InhpeHppdmtyd3F6eGRpeml0dGh2Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MjY1MDQyMjgsImV4cCI6MjA0MjA4MDIyOH0.hux-YkDeO2vYwnMEV2P8KwPohNeXZHoRUOq95aVcq7Q";

        static async Task Main(string[] args)
        {
            var options = new Supabase.SupabaseOptions
            {
                AutoConnectRealtime = true
            };

            _supabaseClient = new Supabase.Client(SupabaseUrl, SupabaseKey, options);
            
            try
            {
                var response = await _supabaseClient.From<ChatMessage>().Get();
                Console.WriteLine("Supabase Connection Test Successful.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supabase Connection Test Failed: {ex.Message}");
            }

            var listener = new TcpListener(IPAddress.Any, 12345);
            listener.Start();

            Console.WriteLine("Server is running...");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _clients.Add(client);
                Console.WriteLine("Client connected.");

                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            string username = "";

            try
            {

                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                username = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                lock(_clientUsernames)
                {
                    _clientUsernames[username] = client;
                }

                Console.WriteLine($"{username} joined the chat.");
                Broadcast($"{username} has joined the chat.");

                await SendChatHistoryAsync(client);

                while (true)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    var timestamp = DateTime.UtcNow.ToLocalTime();

                    if (message.Equals("/users", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendUserListAsync(client);
                    }
                    else
                    {
                        Console.WriteLine($"[{timestamp:g}] {username}: {message}");
                        await SaveMessageAsync(username, message);
                        Broadcast($"[{timestamp:g}] {username}: {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                lock (_clientUsernames)
                {
                    _clientUsernames.Remove(username);
                }
                
                _clients.Remove(client);
                Console.WriteLine($"{username} left the chat.");
                Broadcast($"{username} has left the chat.");
                client.Close();
            }
        }

        private static void Broadcast(string message)
        {
            var buffer = Encoding.ASCII.GetBytes(message);
            foreach (var client in _clients.ToList())
            {
                var stream = client.GetStream();
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();
            }
        }

        private static async Task SendUserListAsync(TcpClient client)
        {
            var stream = client.GetStream();
            string usersList = "Connected users: " + string.Join(", ", _clientUsernames.Keys);
            byte[] buffer = Encoding.ASCII.GetBytes(usersList);
            await stream.WriteAsync(buffer, 0, buffer.Length);
            await stream.FlushAsync();
        }

        private static async Task SendChatHistoryAsync(TcpClient client)
        {
            var history = await _supabaseClient!.From<ChatMessage>()
                .Order("Timestamp", Ordering.Ascending)
                .Get();
            var stream = client.GetStream();

            foreach (var msg in history.Models)
            {
                string fullMessage = $"[{msg.Timestamp:g}] {msg.Username}: {msg.Message} \n";
                byte[] buffer = Encoding.ASCII.GetBytes(fullMessage);
                await stream.WriteAsync(buffer, 0, buffer.Length);
                await stream.FlushAsync();
            }
        }

        private static async Task SaveMessageAsync(string username, string message)
        {
            var chatMessage = new ChatMessage
            {
                Username = username,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            await _supabaseClient!.From<ChatMessage>().Insert(chatMessage);
        }

        public class ChatMessage : BaseModel
        {
            public string? Username { get; set; }
            public string? Message { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
