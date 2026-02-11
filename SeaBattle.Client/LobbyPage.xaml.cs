using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class LobbyPage : Page
    {
        private MainWindow _mainWindow;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private string _playerId;
        private string _playerName;
        private bool _isConnected;
        private CancellationTokenSource _cancellationTokenSource;
        private ObservableCollection<RoomInfo> _rooms = new ObservableCollection<RoomInfo>();
        private string _currentRoomId;

        public LobbyPage(MainWindow mainWindow)
        {
            InitializeComponent();

            _mainWindow = mainWindow;
            _tcpClient = mainWindow.TcpClient;
            _stream = mainWindow.Stream;
            _playerId = mainWindow.PlayerId;
            _playerName = mainWindow.PlayerName;
            _isConnected = true;
            _cancellationTokenSource = new CancellationTokenSource();

            PlayerNameText.Text = _playerName;
            RoomsListBox.ItemsSource = _rooms;

            // Запускаем прослушивание
            Task.Run(() => ListenToServer(_cancellationTokenSource.Token));

            // Запрашиваем список комнат
            _ = RequestRoomsList();
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
                    catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException)
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
                        MessageBox.Show($"Ошибка соединения: {ex.Message}", "Ошибка",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
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

                    case MessageType.Error:
                        MessageBox.Show($"Ошибка: {message.Data?["Message"]}", "Ошибка",
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

        private void HandleRoomCreated(NetworkMessage message)
        {
            var roomId = message.Data?["RoomId"]?.ToString();
            var roomName = message.Data?["RoomName"]?.ToString();

            _currentRoomId = roomId;
            RoomStatusText.Text = $"Вы создали комнату: {roomName}";

            CreateRoomButton.IsEnabled = false;
            LeaveRoomButton.IsEnabled = true;
            StartGameButton.IsEnabled = true;

            _ = RequestRoomsList();
        }

        private void HandleJoinedRoom(NetworkMessage message)
        {
            var roomId = message.Data?["RoomId"]?.ToString();
            var roomName = message.Data?["RoomName"]?.ToString();

            _currentRoomId = roomId;
            RoomStatusText.Text = $"Вы в комнате: {roomName}";

            CreateRoomButton.IsEnabled = false;
            LeaveRoomButton.IsEnabled = true;
            StartGameButton.IsEnabled = true;

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

            RoomStatusText.Text = "Игра началась!";
            StartGameButton.IsEnabled = false;

            // Переходим на страницу игры
            var gamePage = new GamePage(_tcpClient, _stream, _playerId, _playerName, gameData.RoomId);
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.Content = gamePage;
        }

        private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            string roomName = RoomNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                MessageBox.Show("Введите название комнаты", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Ошибка создания комнаты: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
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

                CreateRoomButton.IsEnabled = true;
                LeaveRoomButton.IsEnabled = false;
                StartGameButton.IsEnabled = false;

                _ = RequestRoomsList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выхода из комнаты: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentRoomId))
            {
                MessageBox.Show("Вы не в комнате", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Ошибка начала игры: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Ошибка присоединения к комнате: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Ошибка запроса списка комнат: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Возвращаемся в главное окно, НО НЕ ОТКЛЮЧАЕМСЯ
            var mainWindow = new MainWindow();
            var currentWindow = (MainWindow)Window.GetWindow(this);
            currentWindow.Content = mainWindow.Content;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // НЕ ОТКЛЮЧАЕМСЯ!
        }
    }

    public class RoomInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CreatorName { get; set; }
        public int PlayerCount { get; set; }
        public string Status { get; set; }
    }
}