using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Transactions;


public class Message
{
    public string Title { get; set; }
    public string Content { get; set; }
    public string Type { get; set; }
    public DateTime Timestamp { get; set; }
}

public static class NetworkProtocol
{
    public static async Task SendMessageAsync(NetworkStream client, Message message)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        byte[] lengthPrefix = BitConverter.GetBytes(data.Length);

        await client.WriteAsync(lengthPrefix, 0, 4);
        await client.WriteAsync(data, 0, data.Length);
        await client.FlushAsync();

    }

    public static async Task<Message> ReceiveMessageAsync(NetworkStream client)
    {
        byte[] lengthPrefix = new byte[4];
        await client.ReadExactlyAsync(lengthPrefix, 0, 4);
        int length = BitConverter.ToInt32(lengthPrefix, 0);
        byte[] data = new byte[length];
        await client.ReadExactlyAsync(data, 0, length);
        string json = Encoding.UTF8.GetString(data);
        return System.Text.Json.JsonSerializer.Deserialize<Message>(json);
    }
}

namespace MessegesServer
{
    
    public partial class MainWindow : Window
    {
        private readonly ConcurrentDictionary<NetworkStream, byte> _connectedStreams = new();
        private TcpListener _listener;
        private bool _isRunning = true;
        public MainWindow()
        {
            InitializeComponent();

            StartServer();
        }

        private void StartServer()
        {
            int port = 5000;
            _listener = new TcpListener(System.Net.IPAddress.Any, port);
            _listener.Start();
            Log($"Message server started on port {port}.");
            Task.Run(async () =>
              await AcceptClientAsync(_listener));
        }

        private async Task AcceptClientAsync(TcpListener listener)
        {
            try {
                while (_isRunning)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    Log("Client connected.");
                    NetworkStream stream = client.GetStream();
                    _connectedStreams.TryAdd(stream, 0);
                }
            }
            catch(Exception ex)
            {
                if (_isRunning) { 
                    Log($"Error accepting clients: {ex.Message}");
                }
            }
        }

        private async void Send(object sender, RoutedEventArgs e)
        {
            string title = titleTextBox.Text;
            string content = contentTextBox.Text;
            string type = typeTextBox.Text;

            if (string.IsNullOrWhiteSpace(titleTextBox.Text) && string.IsNullOrWhiteSpace(contentTextBox.Text))
                return;

            Message message = new Message
            {
                Title = title,
                Content = content,
                Type = type,
                Timestamp = DateTime.Now
            };
            await BroadcastMessageAsync(message);
            titleTextBox.Clear();
            contentTextBox.Clear();
            typeTextBox.Clear();
        }

        private async Task BroadcastMessageAsync(Message message)
        {
            var streams = _connectedStreams.Keys;
            foreach (var stream in streams)
            {
                try
                {
                    await NetworkProtocol.SendMessageAsync(stream, message);
                }
                catch (Exception ex)
                {
                    Log($"Error sending message to a client: {ex.Message}");

                    _connectedStreams.TryRemove(stream, out _);
                    stream.Dispose();
                }
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                logTextBlock.Text += $"{DateTime.Now}: {message}\n";
            });
        }
        protected override void OnClosed(EventArgs e)
        {
            _isRunning = false;
            _listener?.Stop(); 

            foreach (var stream in _connectedStreams.Keys)
            {
                try { stream.Dispose(); } catch { }
            }

            base.OnClosed(e);
        }
    }
}
