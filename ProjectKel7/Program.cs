using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ChatShared;

namespace ChatServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var server = new ChatServerApp(5000);
            var cts = new CancellationTokenSource();
            _ = server.StartAsync(cts.Token);

            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
            cts.Cancel();
        }
    }

    public class ChatServerApp
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<string, TcpClient> _clients = new Dictionary<string, TcpClient>();
        private readonly object _gate = new object();

        public ChatServerApp(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public async Task StartAsync(CancellationToken ct)
        {
            _listener.Start();
            Console.WriteLine("[Server] Listening on " + _listener.LocalEndpoint);

            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp = null;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Console.WriteLine("[Server] Accept error: " + ex.Message); continue; }

                _ = Task.Run(() => HandleClientAsync(tcp, ct));
            }
        }

        private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
        {
            string username = null;
            NetworkStream stream = tcp.GetStream();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var msg = await ReadMessageAsync(stream, ct);
                    if (msg == null) break; // client closed

                    if (msg.type == "join")
                    {
                        username = MakeUniqueUsername(string.IsNullOrWhiteSpace(msg.from) ? "Anon" : msg.from);
                        lock (_gate) { _clients[username] = tcp; }
                        Console.WriteLine($"[Server] {username} joined");
                        Log($"{username} joined");
                        await BroadcastAsync(new ChatMessage { type = "sys", text = $"{username} joined", ts = Now() });
                        await BroadcastUsersAsync();
                    }
                    else if (msg.type == "msg")
                    {
                        Console.WriteLine($"[{msg.from}] {msg.text}");
                        Log($"[{msg.from}] {msg.text}");
                        await BroadcastAsync(new ChatMessage { type = "msg", from = msg.from, text = msg.text, ts = Now() });
                    }
                    else if (msg.type == "pm")
                    {
                        if (!string.IsNullOrEmpty(msg.to))
                        {
                            TcpClient target = null;
                            lock (_gate)
                            {
                                if (_clients.TryGetValue(msg.to, out var t)) target = t;
                            }
                            if (target != null)
                            {
                                await SendToAsync(target, new ChatMessage { type = "pm", from = msg.from, to = msg.to, text = msg.text, ts = Now() });
                            }
                        }
                    }
                }
            }
            catch (IOException) { /* connection broken */ }
            catch (Exception ex) { Console.WriteLine("[Server] Error: " + ex.Message); }
            finally
            {
                if (!string.IsNullOrEmpty(username))
                {
                    lock (_gate) { if (_clients.ContainsKey(username)) _clients.Remove(username); }
                    await BroadcastAsync(new ChatMessage { type = "sys", text = $"{username} left", ts = Now() });
                    await BroadcastUsersAsync();
                    Log($"{username} left");
                }
                try { tcp.Close(); } catch { }
            }
        }

        private string MakeUniqueUsername(string desired)
        {
            string name = desired;
            int suffix = 1;
            lock (_gate)
            {
                while (_clients.ContainsKey(name))
                {
                    name = desired + "_" + suffix++;
                }
            }
            return name;
        }

        private async Task BroadcastAsync(ChatMessage m)
        {
            byte[] buf = Serialize(m);
            List<string> deadKeys = new List<string>();

            lock (_gate)
            {
                foreach (var kv in _clients)
                {
                    try
                    {
                        var st = kv.Value.GetStream();
                        st.Write(buf, 0, buf.Length);
                    }
                    catch
                    {
                        deadKeys.Add(kv.Key);
                    }
                }
                foreach (var k in deadKeys) _clients.Remove(k);
            }

            await Task.CompletedTask;
        }

        private async Task SendToAsync(TcpClient tcp, ChatMessage m)
        {
            try
            {
                byte[] buf = Serialize(m);
                tcp.GetStream().Write(buf, 0, buf.Length);
            }
            catch { /* ignore */ }
            await Task.CompletedTask;
        }

        private async Task BroadcastUsersAsync()
        {
            string users;
            lock (_gate) { users = string.Join(",", _clients.Keys); }
            await BroadcastAsync(new ChatMessage { type = "users", text = users, ts = Now() });
        }

        // ---------- framing helpers ----------
        private static byte[] Serialize(ChatMessage m)
        {
            string json = JsonConvert.SerializeObject(m);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(payload.Length); // little-endian
            byte[] buf = new byte[4 + payload.Length];
            Buffer.BlockCopy(len, 0, buf, 0, 4);
            Buffer.BlockCopy(payload, 0, buf, 4, payload.Length);
            return buf;
        }

        private static async Task<ChatMessage> ReadMessageAsync(NetworkStream s, CancellationToken ct)
        {
            byte[] lenBuf = new byte[4];
            int r = await ReadExactAsync(s, lenBuf, 0, 4, ct);
            if (r == 0) return null;
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len <= 0) return null;
            byte[] payload = new byte[len];
            r = await ReadExactAsync(s, payload, 0, len, ct);
            if (r == 0) return null;
            string json = Encoding.UTF8.GetString(payload);
            try
            {
                var msg = JsonConvert.DeserializeObject<ChatMessage>(json);
                return msg;
            }
            catch (JsonException ex)
            {
                Console.WriteLine("[Server] Error parsing JSON: " + ex.Message);
                return null;
            }
        }

        private static async Task<int> ReadExactAsync(NetworkStream s, byte[] buf, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int n = await s.ReadAsync(buf, offset + total, count - total, ct);
                if (n == 0) return 0;
                total += n;
            }
            return total;
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static void Log(string text)
        {
            try { File.AppendAllText("server.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n"); } catch { }
        }
    }
}
