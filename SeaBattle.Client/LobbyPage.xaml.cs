using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class LobbyPage : Page
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private string _playerId;
        private string _playerName;
        private bool _isConnected;
        private CancellationTokenSource _cancellationTokenSource;
        private ObservableCollection<RoomInfo> _rooms = new ObservableCollection<RoomInfo>();
        private string _currentRoomId;

        public LobbyPage(TcpClient tcpClient, NetworkStream stream, string playerId, string playerName)
        {
            InitializeComponent();

            _tcpClient = tcpClient;
            _stream = stream;
            _playerId = playerId;
            _playerName = playerName;
            _isConnected = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Настройка UI
            PlayerNameText.Text = _playerName;
            RoomsListBox.ItemsSource = _rooms;

            // Запускаем прослушивание сообщений
            Task.Run(() => ListenToServer(_cancellationTokenSource.Token));

            // Запрашиваем список комнат
            _ = RequestRoomsList();

            AddChatMessage($"Добро пожаловать в лобби, {_playerName}!");
        }

        private async Task ListenToServer(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[4096];

                while (_isConnected && _tcpClient?.Connected == true)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        // Читаем длину сообщения
                        byte[] lengthBytes = new byte[4];
                        int lengthBytesRead = await _stream.ReadAsync(lengthBytes, 0, 4, cancellationToken);
                        if (lengthBytesRead < 4) break;

                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        // Читаем само сообщение
                        byte[] messageBytes = new byte[messageLength];
                        int totalBytesRead = 0;

                        while (totalBytesRead < messageLength)
                        {
                            int bytesRead = await _stream.ReadAsync(messageBytes, totalBytesRead, messageLength - totalBytesRead, cancellationToken);
                            if (bytesRead == 0) break;
                            totalBytesRead += bytesRead;
                        }

                        string json = Encoding.UTF8.GetString(messageBytes, 0, totalBytesRead);
                        var message = NetworkMessage.FromJson(json);

                        // Обрабатываем сообщение в UI потоке
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
                        AddChatMessage($"Ошибка соединения: {ex.Message}");
                        ReturnToMainWindow();
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
                    case MessageType.RoomCreated:
                        HandleRoomCreated(message);
                        break;

                    case MessageType.JoinedRoom:
                        HandleJoinedRoom(message);
                        break;

                    case MessageType.RoomsList:
                        HandleRoomsList(message);
                        break;

                    case MessageType.StartGame:
                        HandleStartGame(message);
                        break;

                    case MessageType.ChatMessage:
                        HandleChatMessage(message);
                        break;

                    case MessageType.Error:
                        AddChatMessage($"Ошибка: {message.Data?["Message"]}");
                        break;

                    case MessageType.Pong:
                        // Игнорируем
                        break;
                }
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка обработки сообщения: {ex.Message}");
            }
        }

        private void HandleRoomCreated(NetworkMessage message)
        {
            var roomId = message.Data?["RoomId"]?.ToString();
            var roomName = message.Data?["RoomName"]?.ToString();
            var msg = message.Data?["Message"]?.ToString();

            AddChatMessage(msg);

            _currentRoomId = roomId;
            RoomStatusText.Text = $"Вы создали комнату: {roomName}";
            UpdateRoomButtons(true);

            // Обновляем список комнат
            _ = RequestRoomsList();
        }

        private void HandleJoinedRoom(NetworkMessage message)
        {
            var roomId = message.Data?["RoomId"]?.ToString();
            var roomName = message.Data?["RoomName"]?.ToString();
            var msg = message.Data?["Message"]?.ToString();

            AddChatMessage(msg);

            _currentRoomId = roomId;
            RoomStatusText.Text = $"Вы в комнате: {roomName}";
            UpdateRoomButtons(true);

            // Обновляем список комнат
            _ = RequestRoomsList();
        }

        private void HandleRoomsList(NetworkMessage message)
        {
            _rooms.Clear();

            var rooms = message.Data?["Rooms"];
            if (rooms != null)
            {
                foreach (var room in rooms)
                {
                    _rooms.Add(new RoomInfo
                    {
                        Id = room["Id"]?.ToString(),
                        Name = room["Name"]?.ToString(),
                        CreatorName = room["CreatorName"]?.ToString(),
                        PlayerCount = Convert.ToInt32(room["PlayerCount"]),
                        Status = room["Status"]?.ToString()
                    });
                }
            }
        }

        private void HandleStartGame(NetworkMessage message)
        {
            var gameData = message.Data.ToObject<GameStartData>();

            AddChatMessage($"Игра начинается! Противник: {gameData.Player2?.Name ?? "ожидается"}");

            // Здесь будет переход на страницу игры
            // Пока просто выводим информацию
            RoomStatusText.Text = "Игра началась!";
            StartGameButton.IsEnabled = false;
        }

        private void HandleChatMessage(NetworkMessage message)
        {
            var msg = message.Data?["Message"]?.ToString();
            var sender = message.Data?["OriginalSender"]?.ToString();

            if (!string.IsNullOrEmpty(msg))
            {
                if (!string.IsNullOrEmpty(sender))
                {
                    AddChatMessage($"{sender}: {msg}");
                }
                else
                {
                    AddChatMessage($"Сервер: {msg}");
                }
            }
        }

        private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            string roomName = RoomNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                AddChatMessage("Введите название комнаты");
                return;
            }

            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.CreateRoom,
                    SenderId = _playerId,
                    Data = JObject.FromObject(new CreateRoomData
                    {
                        RoomName = roomName
                    })
                };

                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка создания комнаты: {ex.Message}");
            }
        }

        private async void RefreshRoomsButton_Click(object sender, RoutedEventArgs e)
        {
            await RequestRoomsList();
        }

        private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.LeaveRoom,
                    SenderId = _playerId
                };

                await SendMessageAsync(message);

                _currentRoomId = null;
                RoomStatusText.Text = "Не в комнате";
                UpdateRoomButtons(false);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка выхода из комнаты: {ex.Message}");
            }
        }

        private async void StartGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentRoomId))
            {
                AddChatMessage("Вы не в комнате");
                return;
            }

            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.StartGame,
                    SenderId = _playerId,
                    Data = JObject.FromObject(new JoinRoomData
                    {
                        RoomId = _currentRoomId
                    })
                };

                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка начала игры: {ex.Message}");
            }
        }

        private void RoomsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoomsListBox.SelectedItem is RoomInfo selectedRoom)
            {
                JoinRoom(selectedRoom.Id);
            }
        }

        private async void JoinRoom(string roomId)
        {
            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.JoinRoom,
                    SenderId = _playerId,
                    Data = JObject.FromObject(new JoinRoomData
                    {
                        RoomId = roomId
                    })
                };

                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка присоединения к комнате: {ex.Message}");
            }
        }

        private async Task RequestRoomsList()
        {
            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.GetRooms,
                    SenderId = _playerId
                };

                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка запроса списка комнат: {ex.Message}");
            }
        }

        private async void SendChatButton_Click(object sender, RoutedEventArgs e)
        {
            await SendChatMessage();
        }

        private async void ChatTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendChatMessage();
            }
        }

        private async Task SendChatMessage()
        {
            string messageText = ChatTextBox.Text.Trim();
            if (string.IsNullOrEmpty(messageText))
                return;

            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.ChatMessage,
                    SenderId = _playerId,
                    Data = JObject.FromObject(new ChatMessageData
                    {
                        Message = messageText,
                        SenderName = _playerName
                    })
                };

                await SendMessageAsync(message);

                // Отображаем свое сообщение
                AddChatMessage($"Вы: {messageText}");
                ChatTextBox.Text = "";
            }
            catch (Exception ex)
            {
                AddChatMessage($"Ошибка отправки: {ex.Message}");
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

        private void AddChatMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            ChatTextBlock.Text += $"[{timestamp}] {message}\n";

            // Прокручиваем вниз
            var scrollViewer = GetChildOfType<ScrollViewer>(ChatTextBlock.Parent as DependencyObject);
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

        private void UpdateRoomButtons(bool inRoom)
        {
            CreateRoomButton.IsEnabled = !inRoom;
            LeaveRoomButton.IsEnabled = inRoom;
            StartGameButton.IsEnabled = inRoom;

            if (inRoom)
            {
                CreateRoomButton.Opacity = 0.5;
                LeaveRoomButton.Opacity = 1;
                StartGameButton.Opacity = 1;
            }
            else
            {
                CreateRoomButton.Opacity = 1;
                LeaveRoomButton.Opacity = 0.5;
                StartGameButton.Opacity = 0.5;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ReturnToMainWindow();
        }

        private void ReturnToMainWindow()
        {
            _isConnected = false;
            _cancellationTokenSource?.Cancel();

            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.ReturnToMainPage();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isConnected = false;
            _cancellationTokenSource?.Cancel();
        }
    }

    // Классы для отображения в UI
    public class RoomInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CreatorName { get; set; }
        public int PlayerCount { get; set; }
        public string Status { get; set; }
    }
}