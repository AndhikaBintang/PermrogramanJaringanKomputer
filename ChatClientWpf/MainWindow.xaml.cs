using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using Newtonsoft.Json;
using ChatShared;

namespace ChatClientWpf
{
    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private bool _connected = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _client = new TcpClient(TxtIp.Text, int.Parse(TxtPort.Text));
                _stream = _client.GetStream();
                _connected = true;

                AppendMessage($"[SYSTEM] Connected to server.");

                // Kirim join message
                var joinMsg = new ChatMessage
                {
                    From = TxtUsername.Text,
                    Message = $"{TxtUsername.Text} joined",
                    Type = "join",
                    Timestamp = DateTime.Now
                };
                SendMessage(joinMsg);

                // mulai thread terima pesan
                _receiveThread = new Thread(ReceiveMessages);
                _receiveThread.Start();
            }
            catch (Exception ex)
            {
                AppendMessage($"[ERROR] {ex.Message}");
            }
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_connected)
            {
                var msg = new ChatMessage
                {
                    From = TxtUsername.Text,
                    Message = $"{TxtUsername.Text} disconnected",
                    Type = "system",
                    Timestamp = DateTime.Now
                };
                SendMessage(msg);

                _connected = false;
                _stream.Close();
                _client.Close();

                AppendMessage($"[SYSTEM] Disconnected.");
            }
        }

        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) return;

            var msg = new ChatMessage
            {
                From = TxtUsername.Text,
                Message = TxtMessage.Text,
                Type = "chat",
                Timestamp = DateTime.Now
            };

            SendMessage(msg);
            TxtMessage.Clear();
        }

        private void SendMessage(ChatMessage msg)
        {
            try
            {
                string json = JsonConvert.SerializeObject(msg);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                _stream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                AppendMessage($"[ERROR] {ex.Message}");
            }
        }

        private void ReceiveMessages()
        {
            try
            {
                while (_connected)
                {
                    byte[] buffer = new byte[4096];
                    int byteCount = _stream.Read(buffer, 0, buffer.Length);
                    if (byteCount == 0) continue;

                    string data = Encoding.UTF8.GetString(buffer, 0, byteCount);
                    var msg = JsonConvert.DeserializeObject<ChatMessage>(data);

                    if (msg != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string timestamp = msg.Timestamp.ToString("HH:mm:ss");

                            if (msg.Type == "users")
                            {
                                UserList.Items.Clear();
                                var users = msg.Message.Split(',');
                                foreach (var u in users)
                                {
                                    if (!string.IsNullOrWhiteSpace(u))
                                        UserList.Items.Add(u.Trim());
                                }
                            }
                            else
                            {
                                AppendMessage($"[{timestamp}] {msg.From}: {msg.Message}");
                            }
                        });
                    }
                }
            }
            catch
            {
                // ignore error jika client disconnect
            }
        }

        private void AppendMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ChatBox.AppendText(message + Environment.NewLine);
                ChatBox.ScrollToEnd();
            });
        }

        private void btnTheme_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.Resources.MergedDictionaries.Count > 0 &&
                Application.Current.Resources.MergedDictionaries[0].Source != null &&
                Application.Current.Resources.MergedDictionaries[0].Source.OriginalString.Contains("DarkTheme.xaml"))
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative) }
                );
                AppendMessage("[SYSTEM] Theme changed to LightTheme");
            }
            else
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative) }
                );
                AppendMessage("[SYSTEM] Theme changed to DarkTheme");
            }
        }
    }
}
