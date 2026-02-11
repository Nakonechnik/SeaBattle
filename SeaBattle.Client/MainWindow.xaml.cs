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
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private bool _isConnected;
        private CancellationTokenSource _cancellationTokenSource;
        private string _playerId;
        private string _playerName;

        public MainWindow()
        {
            InitializeComponent();
            _isConnected = false;
            _playerId = null;
            _playerName = "Гость";
            UpdateUI();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverAddress = ServerAddressTextBox.Text;
                int port = int.Parse(PortTextBox.Text);
                _playerName = PlayerNameTextBox.Text.Trim();

                if (string.IsNullOrEmpty(_playerName))
                {
                    AddMessage("Введите имя игрока");
                    return;
                }

                AddMessage($"Подключение к {serverAddress}:{port} как {_playerName}...");

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(serverAddress, port);

                _stream = _tcpClient.GetStream();
                _isConnected = true;
                _cancellationTokenSource = new CancellationTokenSource();

                UpdateUI();

                // Запускаем прослушивание сообщений
                _ = Task.Run(() => ListenToServer(_cancellationTokenSource.Token));

                // Отправляем запрос на подключение
                await SendConnectRequest();
            }
            catch (FormatException)
            {
                AddMessage("Ошибка: неверный формат порта");
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка подключения: {ex.Message}");
            }
        }

        private async Task SendConnectRequest()
        {
            var connectMessage = new NetworkMessage
            {
                Type = MessageType.Connect,
                Data = JObject.FromObject(new ConnectData
                {
                    PlayerName = _playerName
                })
            };

            await SendMessageAsync(connectMessage);
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                AddMessage("Сначала подключитесь к серверу");
                return;
            }

            string messageText = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(messageText))
            {
                AddMessage("Введите сообщение");
                return;
            }

            try
            {
                var chatMessage = new NetworkMessage
                {
                    Type = MessageType.ChatMessage,
                    SenderId = _playerId,
                    Data = JObject.FromObject(new ChatMessageData
                    {
                        Message = messageText,
                        SenderName = _playerName
                    })
                };

                await SendMessageAsync(chatMessage);
                AddMessage($"Вы: {messageText}");
                MessageTextBox.Text = "";
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка отправки: {ex.Message}");
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private async void Disconnect()
        {
            try
            {
                if (_isConnected && _stream != null)
                {
                    var disconnectMessage = new NetworkMessage
                    {
                        Type = MessageType.Disconnect
                    };

                    await SendMessageAsync(disconnectMessage);
                }
            }
            catch { }
            finally
            {
                _isConnected = false;
                _cancellationTokenSource?.Cancel();

                _stream?.Close();
                _tcpClient?.Close();

                _stream = null;
                _tcpClient = null;
                _playerId = null;

                AddMessage("Отключено от сервера");
                UpdateUI();

                // Возвращаемся на главную страницу
                ReturnToMainPage();
            }
        }

        private async Task SendMessageAsync(NetworkMessage message)
        {
            if (!_isConnected || _stream == null)
                throw new InvalidOperationException("Не подключено к серверу");

            string json = message.ToJson();
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] length = BitConverter.GetBytes(data.Length);

            await _stream.WriteAsync(length, 0, 4);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }

        private async Task ListenToServer(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[4096];

                while (_isConnected && _tcpClient?.Connected == true && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        byte[] lengthBytes = new byte[4];
                        int lengthBytesRead = await _stream.ReadAsync(lengthBytes, 0, 4, cancellationToken);
                        if (lengthBytesRead < 4) break;

                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                        byte[] messageBytes = new byte[messageLength];
                        int totalBytesRead = 0;

                        while (totalBytesRead < messageLength)
                        {
                            int bytesRead = await _stream.ReadAsync(messageBytes, totalBytesRead,
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
                        AddMessage($"Ошибка соединения: {ex.Message}");
                        Disconnect();
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

                    case MessageType.ChatMessage:
                        HandleChatMessage(message);
                        break;

                    case MessageType.Error:
                        AddMessage($"Ошибка от сервера: {message.Data?["Message"]}");
                        break;

                    case MessageType.Pong:
                        // Игнорируем
                        break;
                }
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка обработки сообщения: {ex.Message}");
            }
        }

        private void HandleConnectResponse(NetworkMessage message)
        {
            var data = message.Data.ToObject<ConnectResponseData>();

            if (data.Success)
            {
                _playerId = data.PlayerId;
                AddMessage($"Успешное подключение! Ваш ID: {_playerId}");

                // !!! ВАЖНО: Переходим в лобби !!!
                NavigateToLobby();
            }
            else
            {
                AddMessage($"Ошибка подключения: {data.Message}");
                Disconnect();
            }
        }

        private void HandleChatMessage(NetworkMessage message)
        {
            var msg = message.Data?["Message"]?.ToString();
            var sender = message.Data?["OriginalSender"]?.ToString();

            if (!string.IsNullOrEmpty(msg))
            {
                if (!string.IsNullOrEmpty(sender))
                {
                    AddMessage($"{sender}: {msg}");
                }
                else
                {
                    AddMessage($"Сервер: {msg}");
                }
            }
        }

        // !!! ВАЖНО: Метод для перехода в лобби !!!
        public void NavigateToLobby()
        {
            // Создаем страницу лобби
            var lobbyPage = new LobbyPage(_tcpClient, _stream, _playerId, _playerName);

            // Получаем родительское окно и меняем содержимое
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                // Меняем содержимое окна на страницу лобби
                mainWindow.Content = lobbyPage;
            }
        }

        public void ReturnToMainPage()
        {
            // Возвращаемся к главному окну
            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();

            // Закрываем текущее окно
            Close();
        }

        private void AddMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            MessageTextBlock.Text += $"[{timestamp}] {message}\n";

            var scrollViewer = GetChildOfType<System.Windows.Controls.ScrollViewer>(MessageTextBlock.Parent as DependencyObject);
            scrollViewer?.ScrollToEnd();
        }

        private T GetChildOfType<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                var result = (child as T) ?? GetChildOfType<T>(child);
                if (result != null) return result;
            }

            return null;
        }

        private void UpdateUI()
        {
            ConnectButton.IsEnabled = !_isConnected;
            SendButton.IsEnabled = _isConnected;
            DisconnectButton.IsEnabled = _isConnected;
            ServerAddressTextBox.IsEnabled = !_isConnected;
            PortTextBox.IsEnabled = !_isConnected;
            PlayerNameTextBox.IsEnabled = !_isConnected;
            MessageTextBox.IsEnabled = _isConnected;

            StatusText.Text = _isConnected ? $"Подключено как {_playerName}" : "Не подключено";
            StatusText.Foreground = _isConnected ?
                System.Windows.Media.Brushes.LightGreen :
                System.Windows.Media.Brushes.LightCoral;
        }

        private void MessageTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && SendButton.IsEnabled)
            {
                SendButton_Click(sender, e);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Disconnect();
            base.OnClosing(e);
        }
    }
}