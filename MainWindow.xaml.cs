using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

namespace MessagesClient
{
    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) return;

            string subscriptionType = TxtSubscription.Text.Trim();
            if (string.IsNullOrEmpty(subscriptionType))
            {
                MessageBox.Show("Будь ласка, введіть тип підписки!");
                return;
            }

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync("127.0.0.1", 5000);
                _stream = _client.GetStream();
                _isConnected = true;

                TxtSubscription.IsEnabled = false;
                BtnConnect.IsEnabled = false;
                BtnConnect.Content = "В мережі";

                byte[] subBytes = Encoding.UTF8.GetBytes(subscriptionType);
                byte[] lengthPrefix = BitConverter.GetBytes(subBytes.Length);
                await _stream.WriteAsync(lengthPrefix, 0, 4);
                await _stream.WriteAsync(subBytes, 0, subBytes.Length);
                await _stream.FlushAsync();

                AppendLogSystemMessage($"Успішно підключено! Підписка: {subscriptionType}");

                _ = Task.Run(async () => await ReceiveMessagesAsync());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося підключитися до сервера: {ex.Message}");
                ResetUI();
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                while (_isConnected)
                {
                    Message msg = await NetworkProtocol.ReceiveMessageAsync(_stream);
                    if (msg == null) break;

                    Dispatcher.Invoke(() => DisplayMessage(msg));
                }
            }
            catch
            {
                Dispatcher.Invoke(() => AppendLogSystemMessage("Зв'язок з сервером втрачено."));
            }
            finally
            {
                Dispatcher.Invoke(() => ResetUI());
            }
        }

        private void DisplayMessage(Message msg)
        {
            Border card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10),
                Background = Brushes.White
            };

            bool isEmergency = msg.Type.Equals("Emergency", StringComparison.OrdinalIgnoreCase);

            if (isEmergency)
            {
                card.BorderBrush = Brushes.Red;
                card.Background = new SolidColorBrush(Color.FromRgb(255, 235, 235)); 
            }
            else
            {
                card.BorderBrush = Brushes.LightGray;
            }

            StackPanel contentPanel = new StackPanel();

            TextBlock titleText = new TextBlock
            {
                Text = (isEmergency ? " [ЕКСТРЕНЕ] " : "") + msg.Title,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = isEmergency ? Brushes.DarkRed : Brushes.Black
            };
            contentPanel.Children.Add(titleText);

            TextBlock metaText = new TextBlock
            {
                Text = $"Тип: {msg.Type} | Час: {msg.Timestamp:HH:mm:ss}",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 5)
            };
            contentPanel.Children.Add(metaText);

            TextBlock bodyText = new TextBlock
            {
                Text = msg.Content,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
            contentPanel.Children.Add(bodyText);

            card.Child = contentPanel;

            MessagesPanel.Children.Insert(0, card);
        }

        private void AppendLogSystemMessage(string text)
        {
            TextBlock log = new TextBlock
            {
                Text = $"⚙ {DateTime.Now:HH:mm:ss}: {text}",
                Foreground = Brushes.Blue,
                Margin = new Thickness(0, 0, 0, 5),
                FontStyle = FontStyles.Italic
            };
            MessagesPanel.Children.Insert(0, log);
        }

        private void ResetUI()
        {
            _isConnected = false;
            TxtSubscription.IsEnabled = true;
            BtnConnect.IsEnabled = true;
            BtnConnect.Content = "🔌 Підключитися";
            _stream?.Dispose();
            _client?.Dispose();
        }

        protected override void OnClosed(EventArgs e)
        {
            ResetUI();
            base.OnClosed(e);
        }
    }
}