using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class MainWindow : Window
    {
        public TcpClient TcpClient { get; private set; }
        public NetworkStream Stream { get; private set; }
        public string PlayerId { get; private set; }
        public string PlayerName { get; private set; }
        public bool IsConnected { get; private set; }

        private CancellationTokenSource _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            IsConnected = false;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverAddress = ServerAddressTextBox.Text;
                int port = int.Parse(PortTextBox.Text);
                PlayerName = PlayerNameTextBox.Text.Trim();

                if (string.IsNullOrEmpty(PlayerName))
                {
                    MessageBox.Show("Введите имя игрока", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ConnectButton.IsEnabled = false;
                ConnectButton.Content = "Подключение...";

                TcpClient = new TcpClient();
                await TcpClient.ConnectAsync(serverAddress, port);

                Stream = TcpClient.GetStream();
                IsConnected = true;
                _cancellationTokenSource = new CancellationTokenSource();

                UpdateUI();

                // Запускаем прослушивание
                _ = Task.Run(() => ListenToServer(_cancellationTokenSource.Token));

                // Отправляем запрос на подключение
                await SendConnectRequest();
            }
            catch (FormatException)
            {
                MessageBox.Show("Неверный формат порта", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Подключиться";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Подключиться";
            }
        }

        private async Task SendConnectRequest()
        {
            var connectMessage = new NetworkMessage
            {
                Type = MessageType.Connect,
                Data = JObject.FromObject(new ConnectData
                {
                    PlayerName = PlayerName
                })
            };

            await SendMessageAsync(connectMessage);
        }

        private async Task SendMessageAsync(NetworkMessage message)
        {
            if (!IsConnected || Stream == null)
                throw new InvalidOperationException("Не подключено к серверу");

            string json = message.ToJson();
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] length = BitConverter.GetBytes(data.Length);

            await Stream.WriteAsync(length, 0, 4);
            await Stream.WriteAsync(data, 0, data.Length);
            await Stream.FlushAsync();
        }

        private async Task ListenToServer(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[4096];

                while (IsConnected && TcpClient?.Connected == true && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        byte[] lengthBytes = new byte[4];
                        int lengthBytesRead = await Stream.ReadAsync(lengthBytes, 0, 4, cancellationToken);
                        if (lengthBytesRead < 4) break;

                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                        byte[] messageBytes = new byte[messageLength];
                        int totalBytesRead = 0;

                        while (totalBytesRead < messageLength)
                        {
                            int bytesRead = await Stream.ReadAsync(messageBytes, totalBytesRead,
                                messageLength - totalBytesRead, cancellationToken);
                            if (bytesRead == 0) break;
                            totalBytesRead += bytesRead;
                        }

                        string json = Encoding.UTF8.GetString(messageBytes, 0, totalBytesRead);
                        var message = NetworkMessage.FromJson(json);

                        await Dispatcher.InvokeAsync(() => ProcessServerMessage(message));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Ошибка соединения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
        }

        private void ProcessServerMessage(NetworkMessage message)
        {
            try
            {
                switch (message.Type)
                {
                    case MessageType.ConnectResponse:
                        HandleConnectResponse(message);
                        break;

                    case MessageType.Error:
                        MessageBox.Show($"Ошибка от сервера: {message.Data?["Message"]}", "Ошибка",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки сообщения: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleConnectResponse(NetworkMessage message)
        {
            var data = message.Data.ToObject<ConnectResponseData>();

            if (data.Success)
            {
                PlayerId = data.PlayerId;
                PlayerName = PlayerNameTextBox.Text;

                StatusText.Text = $"Подключено как {PlayerName}";
                ConnectionStatus.Text = "Подключено";
                ConnectionStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                PlayerNameText.Text = PlayerName;

                // Переходим в лобби
                var lobbyPage = new LobbyPage(this);
                this.Content = lobbyPage;
            }
            else
            {
                MessageBox.Show($"Ошибка подключения: {data.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Подключиться";
            }
        }

        private void UpdateUI()
        {
            ConnectButton.IsEnabled = !IsConnected;
            PlayerNameTextBox.IsEnabled = !IsConnected;
            ServerAddressTextBox.IsEnabled = !IsConnected;
            PortTextBox.IsEnabled = !IsConnected;

            StatusText.Text = IsConnected ? $"Подключено как {PlayerName}" : "Не подключено";
            ConnectionStatus.Text = IsConnected ? "Подключено" : "Отключено";
            ConnectionStatus.Foreground = IsConnected ?
                System.Windows.Media.Brushes.LightGreen :
                System.Windows.Media.Brushes.LightCoral;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
            Application.Current.Shutdown();
        }

        public void Disconnect()
        {
            try
            {
                IsConnected = false;
                _cancellationTokenSource?.Cancel();

                if (Stream != null)
                {
                    Stream.Close();
                    Stream = null;
                }

                if (TcpClient != null)
                {
                    TcpClient.Close();
                    TcpClient = null;
                }
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Disconnect();
            base.OnClosing(e);
        }
    }
}