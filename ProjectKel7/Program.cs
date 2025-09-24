using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ChatServer
{
    public class ChatMessage
    {
        public string type { get; set; }
        public string from { get; set; }
        public string to { get; set; }
        public string text { get; set; }
        public long ts { get; set; }
    }

    class ClientInfo
    {
        public TcpClient Tcp { get; private set; }
        public NetworkStream Stream { get { return Tcp.GetStream(); } }
        public string Username { get; set; }
        public ClientInfo(TcpClient tcp) { Tcp = tcp; }
    }

    class ChatServerApp
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<string, ClientInfo> _clientsByName = new ConcurrentDictionary<string, ClientInfo>();
        private readonly ConcurrentDictionary<TcpClient, ClientInfo> _clients = new ConcurrentDictionary<TcpClient, ClientInfo>();

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
                catch (Exception ex)
                {
                    Console.WriteLine("[Server] Accept error: " + ex.Message);
                    continue;
                }

                if (tcp != null)
                {
                    Console.WriteLine("[Server] New connection");
                    var ci = new ClientInfo(tcp);
                    _clients.TryAdd(tcp, ci);
                    Task.Run(() => HandleClientAsync(ci, ct));
                }
            }

            try { _listener.Stop(); } catch { }
        }

        private async Task HandleClientAsync(ClientInfo ci, CancellationToken ct)
        {
            var stream = ci.Stream;
            try
            {
                var join = await ReceiveMessageAsync(stream, ct);
                if (join == null || join.type != "join")
                {
                    SafeClose(ci);
                    return;
                }

                string desired = string.IsNullOrEmpty(join.from) ? "Anon" : join.from;
                string username = desired;
                int suffix = 1;
                while (!_clientsByName.TryAdd(username, ci))
                {
                    username = desired + "_" + suffix++;
                }
                ci.Username = username;

                Console.WriteLine("[Server] " + username + " joined");
                await BroadcastAsync(new ChatMessage { type = "sys", from = "server", text = username + " joined", ts = Now() });

                while (!ct.IsCancellationRequested)
                {
                    var msg = await ReceiveMessageAsync(stream, ct);
                    if (msg == null) break;

                    if (msg.type == "msg")
                    {
                        var forward = new ChatMessage { type = "msg", from = ci.Username, text = msg.text, ts = Now() };
                        await BroadcastAsync(forward);
                        Console.WriteLine("[" + ci.Username + "] " + msg.text);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Server] Client error: " + ex.Message);
            }
            finally
            {
                await OnClientDisconnect(ci);
            }
        }

        private async Task OnClientDisconnect(ClientInfo ci)
        {
            if (ci == null) return;

            _clients.TryRemove(ci.Tcp, out _);
            if (!string.IsNullOrEmpty(ci.Username))
                _clientsByName.TryRemove(ci.Username, out _);

            await BroadcastAsync(new ChatMessage
            {
                type = "sys",
                from = "server",
                text = (ci.Username ?? "unknown") + " left",
                ts = Now()
            });

            SafeClose(ci);
        }

        public async Task BroadcastAsync(ChatMessage msg)
        {
            byte[] data = Serialize(msg);
            foreach (var kv in _clients)
            {
                try
                {
                    await kv.Value.Stream.WriteAsync(data, 0, data.Length);
                }
                catch { }
            }
        }

        static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        static byte[] Serialize(ChatMessage m)
        {
            string json = JsonConvert.SerializeObject(m);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(payload.Length);
            byte[] buf = new byte[4 + payload.Length];
            Buffer.BlockCopy(len, 0, buf, 0, 4);
            Buffer.BlockCopy(payload, 0, buf, 4, payload.Length);
            return buf;
        }

        static async Task<ChatMessage> ReceiveMessageAsync(NetworkStream s, CancellationToken ct)
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
            try { return JsonConvert.DeserializeObject<ChatMessage>(json); }
            catch { return null; }
        }

        static async Task<int> ReadExactAsync(NetworkStream s, byte[] buf, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int n = await s.ReadAsync(buf, offset + total, count - total);
                if (n == 0) return 0;
                total += n;
            }
            return total;
        }

        static void SafeClose(ClientInfo ci)
        {
            try { ci.Tcp.Close(); } catch { }
        }
    }

    class Program
    {
        static void Main()
        {
            var server = new ChatServerApp(11111);
            var cts = new CancellationTokenSource();
            Task.Run(() => server.StartAsync(cts.Token));

            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
            cts.Cancel();
        }
    }
}
