using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace ChatClientWpf
{
    public class ChatMessage
    {
        public string type { get; set; }
        public string from { get; set; }
        public string to { get; set; }
        public string text { get; set; }
        public long ts { get; set; }
    }

    public partial class MainWindow : Window
    {
        TcpClient _tcp;
        NetworkStream _stream;
        CancellationTokenSource _cts;
        string _username = "";

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            _username = TxtUser.Text.Trim();
            if (string.IsNullOrEmpty(_username))
            {
                MessageBox.Show("Isi username");
                return;
            }

            try
            {
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(IPAddress.Parse(TxtIP.Text), int.Parse(TxtPort.Text));
                _stream = _tcp.GetStream();

                var join = new ChatMessage { type = "join", from = _username, ts = Now() };
                await SendObjectAsync(join);

                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveLoop(_cts.Token)); // pakai _= untuk abaikan warning

                BtnConnect.IsEnabled = false;
                BtnDisconnect.IsEnabled = true;
                BtnSend.IsEnabled = true;
                AddMsg("[system] connected");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connect failed: " + ex.Message);
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            CloseConnection();
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var msg = new ChatMessage { type = "msg", from = _username, text = text, ts = Now() };
            await SendObjectAsync(msg);
            TxtMessage.Clear();
        }

        async Task SendObjectAsync(ChatMessage obj)
        {
            string json = JsonConvert.SerializeObject(obj);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(payload.Length);
            await _stream.WriteAsync(len, 0, 4);
            await _stream.WriteAsync(payload, 0, payload.Length);
        }

        async Task ReceiveLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    byte[] lenBuf = new byte[4];
                    int r = await ReadExactAsync(_stream, lenBuf, 0, 4, ct);
                    if (r == 0) break;

                    int len = BitConverter.ToInt32(lenBuf, 0);
                    byte[] payload = new byte[len];
                    r = await ReadExactAsync(_stream, payload, 0, len, ct);
                    if (r == 0) break;

                    var msg = JsonConvert.DeserializeObject<ChatMessage>(Encoding.UTF8.GetString(payload));
                    Dispatcher.Invoke(() => AddMsg(Format(msg)));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AddMsg("[error] " + ex.Message));
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    BtnConnect.IsEnabled = true;
                    BtnDisconnect.IsEnabled = false;
                    BtnSend.IsEnabled = false;
                });
            }
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

        string Format(ChatMessage m) { return $"[{DateTimeOffset.FromUnixTimeSeconds(m.ts):HH:mm}] {m.from}: {m.text}"; }
        void AddMsg(string text) { LstMessages.Items.Add(text); }

        private void CloseConnection()
        {
            try { _cts?.Cancel(); _stream?.Close(); _tcp?.Close(); }
            catch { }
            finally
            {
                BtnConnect.IsEnabled = true;
                BtnDisconnect.IsEnabled = false;
                BtnSend.IsEnabled = false;
                AddMsg("[system] disconnected");
            }
        }

        static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
    }
}
