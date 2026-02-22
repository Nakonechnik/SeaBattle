using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class GamePage : Page
    {
        private string _roomId;
        private GameBoard _myBoard;
        private GameBoard _enemyBoard;
        private bool _isMyTurn;
        private bool _gameStarted;
        private string _enemyPlayerId;
        private string _enemyPlayerName;

        public GamePage(string roomId)
        {
            InitializeComponent();
            _roomId = roomId;
            _myBoard = new GameBoard();
            _enemyBoard = new GameBoard();
            _gameStarted = false;
            _isMyTurn = false;

            MyBoardControl.GameBoard = _myBoard;
            EnemyBoardControl.GameBoard = _enemyBoard;
            PlayerNameText.Text = App.PlayerName;
            EnemyTitleText.Text = "ПОЛЕ ПРОТИВНИКА";

            UpdateShipsStatus();
            UpdateUIState();

            Task.Run((Func<Task>)ReadLoop);
        }

        private async Task ReadLoop()
        {
            try
            {
                while (!App.Cts.Token.IsCancellationRequested && App.TcpClient.Connected)
                {
                    if (App.Stream.DataAvailable)
                    {
                        byte[] lenBytes = new byte[4];
                        int read = await App.Stream.ReadAsync(lenBytes, 0, 4);
                        if (read < 4) continue;

                        int msgLen = BitConverter.ToInt32(lenBytes, 0);
                        if (msgLen <= 0 || msgLen > 10 * 1024 * 1024) continue;

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
                            await Dispatcher.InvokeAsync(() => ProcessServerMessage(msg));
                        }
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            }
            catch { }
        }

        private void ProcessServerMessage(NetworkMessage message)
        {
            try
            {
                switch (message.Type)
                {
                    case MessageType.GameReady:
                        HandleGameReady(message);
                        break;

                    case MessageType.GameState:
                        HandleGameState(message);
                        break;

                    case MessageType.AttackResult:
                        HandleAttackResult(message);
                        break;

                    case MessageType.TurnChanged:
                        HandleTurnChanged(message);
                        break;

                    case MessageType.GameOver:
                        HandleGameOver(message);
                        break;

                    case MessageType.Error:
                        MessageBox.Show($"Ошибка: {message.Data?["Message"]}", "Ошибка",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                        break;

                    case MessageType.PlayerLeftRoom:
                        HandlePlayerLeftRoom(message);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки сообщения: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleGameReady(NetworkMessage message)
        {
            StatusText.Text = "Противник готов! Ожидание начала игры...";
            StatusText.Foreground = Brushes.LightGreen;
            GamePhaseText.Text = "Ожидание противника";
        }

        private void HandleGameState(NetworkMessage message)
        {
            try
            {
                var gameState = message.Data.ToObject<GameState>();

                _enemyPlayerId = gameState.EnemyPlayerId;
                _enemyPlayerName = gameState.EnemyPlayerName;
                _isMyTurn = gameState.CurrentTurnPlayerId == App.PlayerId;
                _gameStarted = true;

                EnemyTitleText.Text = $"ПОЛЕ {_enemyPlayerName?.ToUpper()}";

                IsEnemyBoardEnabled = _isMyTurn;
                StatusText.Text = _isMyTurn ? "Ваш ход!" : $"Ход {_enemyPlayerName}";
                StatusText.Foreground = _isMyTurn ? Brushes.LightGreen : Brushes.Orange;
                GamePhaseText.Text = "Идет бой";
                SurrenderButton.IsEnabled = true;

                RandomPlaceButton.IsEnabled = false;
                ClearBoardButton.IsEnabled = false;
                ReadyButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки состояния игры: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleAttackResult(NetworkMessage message)
        {
            try
            {
                var result = message.Data.ToObject<AttackResult>();

                if (result.X >= 0 && result.X < 10 && result.Y >= 0 && result.Y < 10)
                {
                    _enemyBoard.Cells[result.X, result.Y] = result.IsHit
                        ? (result.IsDestroyed ? CellState.Destroyed : CellState.Hit)
                        : CellState.Miss;

                    _enemyBoard.VisibleCells[result.X, result.Y] = true;
                    EnemyBoardControl.UpdateBoard();

                    if (message.SenderId == App.PlayerId)
                    {
                        string hitMessage = result.IsDestroyed
                            ? $"Корабль уничтожен! (размер: {result.ShipSize})"
                            : (result.IsHit ? "Попадание!" : "Промах");

                        TurnStatusText.Text = hitMessage;
                        TurnStatusText.Foreground = result.IsHit ? Brushes.LightGreen : Brushes.Orange;
                    }

                    if (result.IsDestroyed)
                    {
                        MessageBox.Show($"Корабль противника уничтожен! Размер: {result.ShipSize}",
                                      "Попадание!", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки результата атаки: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleTurnChanged(NetworkMessage message)
        {
            try
            {
                var data = message.Data.ToObject<TurnChangeData>();
                _isMyTurn = data.NextPlayerId == App.PlayerId;

                IsEnemyBoardEnabled = _isMyTurn;

                if (_isMyTurn)
                {
                    StatusText.Text = "Ваш ход!";
                    StatusText.Foreground = Brushes.LightGreen;
                    TurnStatusText.Text = "Выберите цель для атаки";
                    TurnStatusText.Foreground = Brushes.LightGreen;
                }
                else
                {
                    StatusText.Text = $"Ход {_enemyPlayerName}...";
                    StatusText.Foreground = Brushes.Orange;
                    TurnStatusText.Text = $"Ожидание хода {_enemyPlayerName}";
                    TurnStatusText.Foreground = Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки смены хода: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleGameOver(NetworkMessage message)
        {
            try
            {
                var data = message.Data.ToObject<GameOverData>();
                bool isWinner = data.WinnerId == App.PlayerId;

                string gameOverMessage = isWinner
                    ? "Поздравляем! Вы победили! 🎉"
                    : $"Вы проиграли. Победитель: {data.WinnerName}";

                MessageBox.Show(gameOverMessage, "Игра окончена",
                              MessageBoxButton.OK, isWinner ? MessageBoxImage.Information : MessageBoxImage.Exclamation);

                IsEnemyBoardEnabled = false;
                SurrenderButton.IsEnabled = false;
                StatusText.Text = isWinner ? "Победа!" : "Поражение";
                StatusText.Foreground = isWinner ? Brushes.Gold : Brushes.LightCoral;
                GamePhaseText.Text = "Игра окончена";

                Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ReturnToLobby();
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки конца игры: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandlePlayerLeftRoom(NetworkMessage message)
        {
            string playerName = message.Data?["PlayerName"]?.ToString() ?? "Противник";
            MessageBox.Show($"{playerName} покинул игру!", "Информация",
                          MessageBoxButton.OK, MessageBoxImage.Information);

            ReturnToLobby();
        }

        private void MyBoardCellClicked(object sender, CellClickEventArgs e)
        {
            if (_gameStarted)
            {
                MessageBox.Show("Игра уже началась, расстановка невозможна!", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var shipToPlace = _myBoard.Ships.FirstOrDefault(s => !s.IsPlaced);
            if (shipToPlace == null)
            {
                MessageBox.Show("Все корабли уже размещены!", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_myBoard.CanPlaceShip(e.X, e.Y, shipToPlace.Size, true))
            {
                _myBoard.PlaceShip(shipToPlace, e.X, e.Y, true);
                MyBoardControl.UpdateBoard();
                UpdateShipsStatus();
            }
            else if (_myBoard.CanPlaceShip(e.X, e.Y, shipToPlace.Size, false))
            {
                _myBoard.PlaceShip(shipToPlace, e.X, e.Y, false);
                MyBoardControl.UpdateBoard();
                UpdateShipsStatus();
            }
            else
            {
                MessageBox.Show("Нельзя разместить корабль здесь! Корабли не должны касаться друг друга.", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnemyBoardCellClicked(object sender, CellClickEventArgs e)
        {
            if (!_gameStarted)
            {
                MessageBox.Show("Игра еще не началась!", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!_isMyTurn)
            {
                MessageBox.Show("Сейчас не ваш ход!", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_enemyBoard.VisibleCells[e.X, e.Y])
            {
                MessageBox.Show("В эту клетку уже стреляли!", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SendAttackAsync(e.X, e.Y);
        }

        private async void SendAttackAsync(int x, int y)
        {
            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.Attack,
                    SenderId = App.PlayerId,
                    Data = JObject.FromObject(new AttackData
                    {
                        RoomId = _roomId,
                        X = x,
                        Y = y
                    })
                };

                await SendMessageAsync(message);

                IsEnemyBoardEnabled = false;
                TurnStatusText.Text = "Выстрел...";
                TurnStatusText.Foreground = Brushes.White;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки атаки: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                IsEnemyBoardEnabled = true;
            }
        }

        private async Task SendMessageAsync(NetworkMessage message)
        {
            string json = message.ToJson();
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] length = BitConverter.GetBytes(data.Length);

            await App.Stream.WriteAsync(length, 0, 4);
            await App.Stream.WriteAsync(data, 0, data.Length);
            await App.Stream.FlushAsync();
        }

        private void RandomPlaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameStarted) return;

            _myBoard = new GameBoard();
            _myBoard.RandomPlacement();
            MyBoardControl.GameBoard = _myBoard;
            MyBoardControl.UpdateBoard();
            UpdateShipsStatus();
        }

        private void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameStarted) return;

            _myBoard = new GameBoard();
            MyBoardControl.GameBoard = _myBoard;
            MyBoardControl.ResetBoard();
            UpdateShipsStatus();
            ReadyButton.IsEnabled = false;
        }

        private async void ReadyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_myBoard.IsReady)
            {
                int placed = _myBoard.Ships.Count(s => s.IsPlaced);
                int total = _myBoard.Ships.Count;
                MessageBox.Show($"Разместите все корабли! Размещено {placed} из {total}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.GameReady,
                    SenderId = App.PlayerId,
                    Data = JObject.FromObject(new
                    {
                        RoomId = _roomId,
                        Board = _myBoard
                    })
                };

                await SendMessageAsync(message);

                ReadyButton.IsEnabled = false;
                RandomPlaceButton.IsEnabled = false;
                ClearBoardButton.IsEnabled = false;
                StatusText.Text = "Готов к игре! Ожидание противника...";
                StatusText.Foreground = Brushes.Orange;
                GamePhaseText.Text = "Ожидание противника";

                MessageBox.Show("Вы готовы к игре. Ожидайте готовности противника.",
                              "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки готовности: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SurrenderButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Вы действительно хотите сдаться?", "Подтверждение",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var surrenderMessage = new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = App.PlayerId,
                        Data = JObject.FromObject(new
                        {
                            RoomId = _roomId,
                            WinnerId = _enemyPlayerId,
                            WinnerName = _enemyPlayerName,
                            IsSurrender = true
                        })
                    };

                    await SendMessageAsync(surrenderMessage);

                    MessageBox.Show("Вы сдались. Игра окончена.", "Информация",
                                  MessageBoxButton.OK, MessageBoxImage.Information);

                    ReturnToLobby();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExitGameButton_Click(object sender, RoutedEventArgs e)
        {
            ExitGame();
        }

        private async void ExitGame()
        {
            var result = MessageBox.Show("Вы действительно хотите выйти из игры?", "Подтверждение",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var leaveMessage = new NetworkMessage
                    {
                        Type = MessageType.LeaveRoom,
                        SenderId = App.PlayerId
                    };

                    await SendMessageAsync(leaveMessage);
                }
                catch { }

                ReturnToLobby();
            }
        }

        private void ReturnToLobby()
        {
            var window = Window.GetWindow(this);
            if (window is MainWindow mainWindow)
            {
                mainWindow.Content = new LobbyPage();
            }
        }

        private void UpdateShipsStatus()
        {
            int placed = _myBoard.Ships.Count(s => s.IsPlaced);
            int total = _myBoard.Ships.Count;
            ShipsStatusText.Text = $"{placed}/{total} размещено";

            if (_myBoard.IsReady)
            {
                ReadyButton.IsEnabled = !_gameStarted;
                StatusText.Text = "Все корабли размещены! Нажмите 'Готов'";
                StatusText.Foreground = Brushes.LightGreen;
            }
            else
            {
                ReadyButton.IsEnabled = false;
                StatusText.Text = "Расстановка кораблей";
                StatusText.Foreground = Brushes.Yellow;
            }
        }

        private void UpdateUIState()
        {
            if (_gameStarted)
            {
                RandomPlaceButton.IsEnabled = false;
                ClearBoardButton.IsEnabled = false;
                ReadyButton.IsEnabled = false;
                SurrenderButton.IsEnabled = true;
            }
            else
            {
                RandomPlaceButton.IsEnabled = true;
                ClearBoardButton.IsEnabled = true;
                SurrenderButton.IsEnabled = false;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // Страница выгружается, но соединение остается для App
        }

        public bool IsEnemyBoardEnabled
        {
            get { return EnemyBoardControl.IsEnabled; }
            set
            {
                EnemyBoardControl.IsEnabled = value;
                if (TurnStatusText != null)
                {
                    TurnStatusText.Text = value ? "Ваш ход!" : "Ход противника...";
                }
            }
        }
    }
}