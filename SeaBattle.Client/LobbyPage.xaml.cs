using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public class ClientRoomInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CreatorId { get; set; }
        public string CreatorName { get; set; }
        public int PlayerCount { get; set; }
        public string Status { get; set; }
        public bool IsMyRoom
        {
            get { return CreatorId == App.PlayerId; }
        }

        public Brush StatusColor
        {
            get
            {
                if (Status == "Waiting")
                    return Brushes.Orange;
                else if (Status == "Full")
                    return Brushes.LightGreen;
                else if (Status == "InGame")
                    return Brushes.Red;
                else
                    return Brushes.Gray;
            }
        }
    }

    public partial class LobbyPage : Page
    {
        private ObservableCollection<ClientRoomInfo> _myRooms = new ObservableCollection<ClientRoomInfo>();
        private ObservableCollection<ClientRoomInfo> _availableRooms = new ObservableCollection<ClientRoomInfo>();
        private string _currentRoomId;
        private bool _isReading = false;

        public LobbyPage()
        {
            InitializeComponent();

            MyRoomsListBox.ItemsSource = _myRooms;
            AvailableRoomsListBox.ItemsSource = _availableRooms;
            PlayerNameText.Text = App.PlayerName;

            // Запускаем чтение сообщений (только один раз)
            if (!_isReading)
            {
                _isReading = true;
                Task.Run((Func<Task>)ReadLoop);
            }

            // Запрашиваем список комнат
            Task.Delay(500).ContinueWith(_ =>
            {
                Dispatcher.InvokeAsync(() => GetRooms());
            });
        }

        private async Task ReadLoop()
        {
            try
            {
                while (!App.Cts.Token.IsCancellationRequested && App.TcpClient.Connected)
                {
                    if (App.Stream.DataAvailable)
                    {
                        // Читаем длину
                        byte[] lenBytes = new byte[4];
                        int read = await App.Stream.ReadAsync(lenBytes, 0, 4);
                        if (read < 4) continue;

                        int msgLen = BitConverter.ToInt32(lenBytes, 0);
                        if (msgLen <= 0 || msgLen > 10 * 1024 * 1024) continue;

                        // Читаем тело
                        byte[] msgData = new byte[msgLen];
                        int totalRead = 0;
                        while (totalRead < msgLen)
                        {
                            int r = await App.Stream.ReadAsync(msgData, totalRead, msgLen - totalRead);
                            if (r == 0) break;
                            totalRead += r;
                        }

                        string json = Encoding.UTF8.GetString(msgData);
                        var message = NetworkMessage.FromJson(json);

                        if (message != null)
                        {
                            var msg = message;
                            await Dispatcher.InvokeAsync(() => HandleMessage(msg));
                        }
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ReadLoop error: {ex.Message}");
            }
        }

        private void HandleMessage(NetworkMessage msg)
        {
            switch (msg.Type)
            {
                case MessageType.RoomsList:
                    UpdateRoomsList(msg.Data["rooms"] as JArray);
                    break;

                case MessageType.RoomCreated:
                    var newRoomId = msg.Data["RoomId"]?.ToString() ?? msg.Data["roomId"]?.ToString();
                    var newRoomName = msg.Data["RoomName"]?.ToString() ?? msg.Data["roomName"]?.ToString();
                    _currentRoomId = newRoomId;
                    RoomStatusText.Text = $"Вы создали комнату: {newRoomName}";
                    CreateRoomButton.IsEnabled = false;
                    DeleteRoomButton.IsEnabled = true;
                    _ = GetRooms();
                    break;

                case MessageType.JoinedRoom:
                    var roomId = msg.Data["RoomId"]?.ToString() ?? msg.Data["roomId"]?.ToString();
                    var roomName = msg.Data["RoomName"]?.ToString() ?? msg.Data["roomName"]?.ToString();
                    _currentRoomId = roomId;
                    RoomStatusText.Text = $"Вы в комнате: {roomName}";
                    CreateRoomButton.IsEnabled = false;

                    var myRoom = _myRooms.FirstOrDefault(r => r.Id == roomId);
                    DeleteRoomButton.IsEnabled = myRoom != null && myRoom.IsMyRoom;

                    _ = GetRooms();
                    break;

                case MessageType.StartGame:
                    var gameData = msg.Data.ToObject<GameStartData>();
                    RoomStatusText.Text = "Игра начинается!";

                    // ИСПРАВЛЕНИЕ: Используем Window.Content вместо NavigationService
                    Dispatcher.InvokeAsync(() =>
                    {
                        _currentRoomId = null;
                        CreateRoomButton.IsEnabled = true;
                        DeleteRoomButton.IsEnabled = false;

                        var gamePage = new GamePage(gameData.RoomId);
                        var window = Window.GetWindow(this);
                        if (window is MainWindow mainWindow)
                        {
                            mainWindow.Content = gamePage;
                        }
                    });
                    break;

                case MessageType.PlayerJoinedRoom:
                    var playerName = msg.Data["PlayerName"]?.ToString() ?? "Игрок";
                    RoomStatusText.Text = $"{playerName} присоединился!";
                    _ = GetRooms();
                    break;

                case MessageType.PlayerLeftRoom:
                    RoomStatusText.Text = "Игрок покинул комнату";
                    _currentRoomId = null;
                    CreateRoomButton.IsEnabled = true;
                    DeleteRoomButton.IsEnabled = false;
                    _ = GetRooms();
                    break;
            }
        }

        private void UpdateRoomsList(JArray rooms)
        {
            _myRooms.Clear();
            _availableRooms.Clear();

            if (rooms == null) return;

            foreach (var room in rooms)
            {
                var info = new ClientRoomInfo
                {
                    Id = room["id"]?.ToString() ?? room["Id"]?.ToString(),
                    Name = room["name"]?.ToString() ?? room["Name"]?.ToString() ?? "Без имени",
                    CreatorId = room["creatorId"]?.ToString() ?? room["CreatorId"]?.ToString(),
                    CreatorName = room["creatorName"]?.ToString() ?? room["CreatorName"]?.ToString() ?? "Неизвестно",
                    PlayerCount = room["playerCount"]?.Value<int>() ?? room["PlayerCount"]?.Value<int>() ?? 0,
                    Status = room["status"]?.ToString() ?? room["Status"]?.ToString() ?? "Waiting"
                };

                if (info.IsMyRoom)
                {
                    _myRooms.Add(info);
                    // Если это моя комната и я в ней, активируем кнопку удаления
                    if (info.Id == _currentRoomId)
                    {
                        DeleteRoomButton.IsEnabled = true;
                    }
                }
                else if (info.PlayerCount < 2 && info.Status != "InGame")
                {
                    _availableRooms.Add(info);
                }
            }

            ConnectionStatusText.Text = $"Моих: {_myRooms.Count}, Доступно: {_availableRooms.Count}";
        }

        private async Task SendMessage(NetworkMessage msg)
        {
            try
            {
                string json = msg.ToJson();
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] len = BitConverter.GetBytes(data.Length);

                await App.Stream.WriteAsync(len, 0, 4);
                await App.Stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send error: {ex.Message}");
            }
        }

        private async Task GetRooms()
        {
            await SendMessage(new NetworkMessage { Type = MessageType.GetRooms, SenderId = App.PlayerId });
        }

        private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RoomNameTextBox.Text))
            {
                MessageBox.Show("Введите название комнаты", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrEmpty(_currentRoomId))
            {
                MessageBox.Show("Вы уже находитесь в комнате", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await SendMessage(new NetworkMessage
            {
                Type = MessageType.CreateRoom,
                SenderId = App.PlayerId,
                Data = JObject.FromObject(new { roomName = RoomNameTextBox.Text.Trim() })
            });

            RoomStatusText.Text = "Создание комнаты...";
        }

        private async void JoinRoom(string roomId)
        {
            if (!string.IsNullOrEmpty(_currentRoomId))
            {
                MessageBox.Show("Вы уже находитесь в комнате", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await SendMessage(new NetworkMessage
            {
                Type = MessageType.JoinRoom,
                SenderId = App.PlayerId,
                Data = JObject.FromObject(new { roomId })
            });
        }

        private async void DeleteRoomButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentRoomId))
            {
                MessageBox.Show("Вы не в комнате", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Проверяем, что это действительно наша комната
            var myRoom = _myRooms.FirstOrDefault(r => r.Id == _currentRoomId);
            if (myRoom == null || !myRoom.IsMyRoom)
            {
                MessageBox.Show("Вы можете удалить только свою комнату", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (MessageBox.Show($"Удалить комнату '{myRoom.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await SendMessage(new NetworkMessage
                {
                    Type = MessageType.LeaveRoom,
                    SenderId = App.PlayerId
                });

                // Сбрасываем состояние
                _currentRoomId = null;
                CreateRoomButton.IsEnabled = true;
                DeleteRoomButton.IsEnabled = false;
                RoomStatusText.Text = "Не в комнате";

                // Обновляем список
                await GetRooms();
            }
        }

        private async void RefreshRoomsButton_Click(object sender, RoutedEventArgs e)
        {
            await GetRooms();
        }

        private void AvailableRoomsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AvailableRoomsListBox.SelectedItem is ClientRoomInfo room)
            {
                if (string.IsNullOrEmpty(_currentRoomId))
                {
                    if (room.PlayerCount < 2 && room.Status != "InGame")
                    {
                        if (MessageBox.Show($"Присоединиться к комнате '{room.Name}'?", "Подтверждение",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            JoinRoom(room.Id);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Эта комната недоступна", "Информация",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("Вы уже находитесь в комнате", "Информация",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                AvailableRoomsListBox.SelectedItem = null;
            }
        }

        private void MyRoomsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MyRoomsListBox.SelectedItem is ClientRoomInfo room)
            {
                if (string.IsNullOrEmpty(_currentRoomId))
                {
                    // Если не в комнате, можно войти в свою комнату
                    if (room.Status != "InGame")
                    {
                        if (MessageBox.Show($"Войти в свою комнату '{room.Name}'?", "Подтверждение",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            JoinRoom(room.Id);
                        }
                    }
                }
                else if (room.Id == _currentRoomId && room.IsMyRoom)
                {
                    // Если это текущая комната и она моя, активируем кнопку удаления
                    DeleteRoomButton.IsEnabled = true;
                }

                MyRoomsListBox.SelectedItem = null;
            }
        }

        private void DiagnosticButton_Click(object sender, RoutedEventArgs e)
        {
            string info = $"ID: {App.PlayerId}\n" +
                         $"Комнат моих: {_myRooms.Count}\n" +
                         $"Доступно: {_availableRooms.Count}\n" +
                         $"Connected: {App.TcpClient?.Connected}\n" +
                         $"CurrentRoom: {_currentRoomId ?? "none"}\n" +
                         $"CreateBtn: {CreateRoomButton.IsEnabled}\n" +
                         $"DeleteBtn: {DeleteRoomButton.IsEnabled}";
            MessageBox.Show(info, "Диагностика");
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            App.Cts.Cancel();
            App.Stream?.Close();
            App.TcpClient?.Close();

            var newMainWindow = new MainWindow();
            Application.Current.MainWindow = newMainWindow;
            newMainWindow.Show();
            Window.GetWindow(this)?.Close();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // Не отменяем токен здесь
        }
    }
}