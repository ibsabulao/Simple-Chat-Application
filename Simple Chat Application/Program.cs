using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ChatAppClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Write("Enter your username: ");
            string? username = Console.ReadLine();

            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 12345);
            var stream = client.GetStream();

            byte[] buffer = Encoding.ASCII.GetBytes(username!);
            await stream.WriteAsync(buffer, 0, buffer.Length);

            _ = Task.Run(() => ReceiveMessagesAsync(client));

            Console.WriteLine("You can now start chatting.");

            while (true)
            {
                string? message = Console.ReadLine();
                await stream.WriteAsync(Encoding.ASCII.GetBytes(message!), 0, message!.Length);
            }
        }

        private static async Task ReceiveMessagesAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Console.WriteLine(message);
            }
        }
    }
}
