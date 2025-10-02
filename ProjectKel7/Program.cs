using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using ChatShared;

namespace ChatServer
{
    class Program
    {
        private static TcpListener _listener;
        private static readonly Dictionary<TcpClient, string> _clients = new Dictionary<TcpClient, string>();
        private static readonly object _lock = new object();

        static void Main(string[] args)
        {
            int port = 5000;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            Console.WriteLine($"[Server] Listening on {port}...");

            while (true)
            {
                var client = _listener.AcceptTcpClient();
                lock (_lock)
                {
                    _clients.Add(client, ""); // Username diisi setelah join
                }
                Thread t = new Thread(HandleClient);
                t.Start(client);
            }
        }

        private static void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            string username = "Unknown";

            try
            {
                while (true)
                {
                    byte[] buffer = new byte[4096];
                    int byteCount = stream.Read(buffer, 0, buffer.Length);
                    if (byteCount == 0) break; // Klien disconnect

                    string data = Encoding.UTF8.GetString(buffer, 0, byteCount);
                    ChatMessage msg = JsonConvert.DeserializeObject<ChatMessage>(data);

                    if (msg != null)
                    {
                        msg.Timestamp = DateTime.Now;

                        if (msg.Type == "join")
                        {
                            username = msg.From;
                            lock (_lock)
                            {
                                _clients[client] = username;
                            }

                            Console.WriteLine($"[{msg.Timestamp:HH:mm:ss}] {username} joined");

                            Broadcast(new ChatMessage
                            {
                                From = "SYSTEM",
                                Message = $"{username} joined",
                                Type = "system",
                                Timestamp = msg.Timestamp
                            });

                            BroadcastUsers();
                        }
                        else if (msg.Type == "chat")
                        {
                            Console.WriteLine($"[{msg.Timestamp:HH:mm:ss}] {username}: {msg.Message}");
                            Broadcast(msg);
                        }
                        else if (msg.Type == "system")
                        {
                            Console.WriteLine($"[{msg.Timestamp:HH:mm:ss}] [SYSTEM] {msg.Message}");
                            Broadcast(msg);
                        }
                        else if (msg.Type == "pm")
                        {
                            Console.WriteLine($"[{msg.Timestamp:HH:mm:ss}] (PM) {msg.From} -> {msg.To}: {msg.Message}");
                            SendPrivate(msg);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // abaikan error
            }
            finally
            {
                lock (_lock)
                {
                    if (_clients.ContainsKey(client))
                    {
                        _clients.Remove(client);
                    }
                }
                client.Close();

                Broadcast(new ChatMessage
                {
                    From = "SYSTEM",
                    Message = $"{username} disconnected",
                    Type = "system",
                    Timestamp = DateTime.Now
                });

                BroadcastUsers();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {username} disconnected");
            }
        }

        private static void Broadcast(ChatMessage msg)
        {
            string json = JsonConvert.SerializeObject(msg);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            List<TcpClient> clientsCopy;

            lock (_lock)
            {
                clientsCopy = new List<TcpClient>(_clients.Keys);
            }

            foreach (var c in clientsCopy)
            {
                try
                {
                    NetworkStream stream = c.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch { }
            }
        }

        private static void BroadcastUsers()
        {
            string allUsers;
            lock (_lock)
            {
                allUsers = string.Join(",", _clients.Values.Where(u => !string.IsNullOrEmpty(u)));
            }

            var msg = new ChatMessage
            {
                From = "SYSTEM",
                Message = allUsers,
                Type = "users",
                Timestamp = DateTime.Now
            };

            Broadcast(msg);
        }

        private static void SendPrivate(ChatMessage msg)
        {
            string json = JsonConvert.SerializeObject(msg);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            TcpClient targetClient = null;
            TcpClient senderClient = null;

            lock (_lock)
            {
                targetClient = _clients.FirstOrDefault(x => x.Value == msg.To).Key;
                senderClient = _clients.FirstOrDefault(x => x.Value == msg.From).Key;
            }

            if (targetClient != null)
            {
                try
                {
                    NetworkStream stream = targetClient.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch { }
            }

            if (senderClient != null)
            {
                try
                {
                    NetworkStream stream = senderClient.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch { }
            }
        }
    }
}
