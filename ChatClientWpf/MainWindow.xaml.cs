using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using ChatShared;
using System.Net.Sockets;

namespace ChatClientWpf
{
    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private string _username;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ip = txtIP.Text.Trim();
                int port = int.Parse(txtPort.Text.Trim());
                _username = txtUsername.Text.Trim();

                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();

                AppendChat("[SYSTEM] Connected to server.");

                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveLoop(_cts.Token));

                // send join message (length-prefixed JSON)
                await SendObjectAsync(new ChatMessage { type = "join", from = _username });
            }
            catch (Exception ex)
            {
                AppendChat("[ERROR] " + ex.Message);
            }
        }

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_client != null && _client.Connected)
                {
                    await SendObjectAsync(new ChatMessage { type = "leave", from = _username });
                }
            }
            catch { }
            finally
            {
                try { _cts?.Cancel(); _stream?.Close(); _client?.Close(); } catch { }
                AppendChat("[SYSTEM] Disconnected.");
            }
        }

        private async void btnSend_Click(object sender, RoutedEventArgs e)
        {
            string text = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (text.StartsWith("/w "))
            {
                var parts = text.Split(new[] { ' ' }, 3);
                if (parts.Length >= 3)
                {
                    var pm = new ChatMessage { type = "pm", from = _username, to = parts[1], text = parts[2] };
                    await SendObjectAsync(pm);
                    AppendChat($"[PM to {parts[1]}] {parts[2]}");
                }
            }
            else
            {
                var m = new ChatMessage { type = "msg", from = _username, text = text };
                await SendObjectAsync(m);
            }

            txtMessage.Clear();
        }

        private async Task SendObjectAsync(ChatMessage msg)
        {
            if (_stream == null) return;
            string json = JsonConvert.SerializeObject(msg);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(payload.Length);
            try
            {
                await _stream.WriteAsync(len, 0, 4);
                await _stream.WriteAsync(payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                AppendChat("[ERROR] Send failed: " + ex.Message);
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // read length
                    byte[] lenBuf = new byte[4];
                    int r = await ReadExactAsync(_stream, lenBuf, 0, 4, ct);
                    if (r == 0) break;
                    int len = BitConverter.ToInt32(lenBuf, 0);
                    if (len <= 0) continue;

                    byte[] payload = new byte[len];
                    r = await ReadExactAsync(_stream, payload, 0, len, ct);
                    if (r == 0) break;

                    string json = Encoding.UTF8.GetString(payload);
                    ChatMessage msg = null;
                    try { msg = JsonConvert.DeserializeObject<ChatMessage>(json); }
                    catch (JsonException)
                    {
                        AppendChat("[ERROR] Received invalid JSON");
                        continue;
                    }

                    if (msg == null) continue;

                    // handle
                    if (msg.type == "users")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            userList.Items.Clear();
                            if (!string.IsNullOrEmpty(msg.text))
                            {
                                string[] arr = msg.text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var u in arr) userList.Items.Add(u);
                            }
                        });
                    }
                    else if (msg.type == "msg")
                    {
                        AppendChat($"[{msg.from}] {msg.text}");
                    }
                    else if (msg.type == "pm")
                    {
                        AppendChat($"[PM from {msg.from}] {msg.text}");
                    }
                    else if (msg.type == "sys")
                    {
                        AppendChat($"[SYSTEM] {msg.text}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendChat("[SYSTEM] Receive loop ended: " + ex.Message);
            }
            finally
            {
                Dispatcher.Invoke(() => AppendChat("[SYSTEM] Connection closed."));
            }
        }

        private static async Task<int> ReadExactAsync(NetworkStream s, byte[] buf, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int read = await s.ReadAsync(buf, offset + total, count - total, ct);
                if (read == 0) return 0;
                total += read;
            }
            return total;
        }

        private void AppendChat(string text)
        {
            Dispatcher.Invoke(() =>
            {
                chatBox.AppendText(text + Environment.NewLine);
                chatBox.ScrollToEnd();
            });
        }

        // theme button behavior (simple swap using merged dictionaries)
        private void btnTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var md = Application.Current.Resources.MergedDictionaries;
                if (md.Count > 0 && md[0].Source != null && md[0].Source.OriginalString.Contains("LightTheme"))
                {
                    md.Clear();
                    md.Add(new System.Windows.ResourceDictionary { Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative) });
                }
                else
                {
                    md.Clear();
                    md.Add(new System.Windows.ResourceDictionary { Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative) });
                }
            }
            catch { }
        }
    }
}
