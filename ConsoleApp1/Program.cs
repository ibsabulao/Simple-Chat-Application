using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChatAppServer
{
    class Program
    {
        private static readonly List<TcpClient> _clients = new List<TcpClient>();
        private static readonly MongoClient _mongoClient = new MongoClient("mongodb+srv://irenesabulao:G2GOOBSDoHo4mrNH@chatcluster.8ocgn.mongodb.net/?retryWrites=true&w=majority&appName=ChatCluster");
        private static readonly IMongoDatabase _database = _mongoClient.GetDatabase("ChatApp");
        private static readonly IMongoCollection<ChatMessage> _collection = _database.GetCollection<ChatMessage>("Messages");

        static async Task Main(string[] args)
        {
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
            var buffer = new byte[1024];
            string username = "";

            try
            {

                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                username = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                Console.WriteLine($"{username} joined the chat.");

                Broadcast($"{username} has joined the chat.");

                await SendChatHistoryAsync(client);

                while (true)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    Console.WriteLine($"{username}: {message}");

                    await SaveMessageAsync(username, message);

                    Broadcast($"{username}: {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
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

        private static async Task SendChatHistoryAsync(TcpClient client)
        {
            var history = await _collection
                .Find(_ => true)
                .SortBy(m => m.Timestamp)
                .ToListAsync();
            var stream = client.GetStream();

            foreach (var msg in history)
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
                Timestamp = DateTime.Now
            };

            await _collection.InsertOneAsync(chatMessage);
        }
    }

    public class ChatMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string Username { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
