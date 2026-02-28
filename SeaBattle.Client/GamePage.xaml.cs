using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
        private bool _gameOverHandled;
        private bool _placeShipHorizontal = true;
        private DispatcherTimer _turnTimer;
        private int _secondsLeft;
        private bool _isReconnect;

        public GamePage(string roomId, bool isReconnect = false)
        {
            InitializeComponent();
            _roomId = roomId;
            _isReconnect = isReconnect;
            _myBoard = new GameBoard();
            _enemyBoard = new GameBoard();
            _gameStarted = false;
            _isMyTurn = false;

            MyBoardControl.GameBoard = _myBoard;
            EnemyBoardControl.GameBoard = _enemyBoard;
            PlayerNameText.Text = App.PlayerName;
            EnemyTitleText.Text = "ПОЛЕ ПРОТИВНИКА";

            _turnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _turnTimer.Tick += TurnTimer_Tick;

            UpdateShipsStatus();
            UpdateUIState();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isReconnect)
            {
                _isReconnect = false;
                await SendReconnectToGameAsync();
            }
        }

        public void ProcessServerMessage(NetworkMessage message)
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
                        if (_gameStarted && _isMyTurn)
                            IsEnemyBoardEnabled = true;
                        break;

                    case MessageType.PlayerLeftRoom:
                        HandlePlayerLeftRoom(message);
                        break;

                    case MessageType.OpponentDisconnected:
                        HandleOpponentDisconnected(message);
                        break;

                    case MessageType.OpponentReconnected:
                        HandleOpponentReconnected(message);
                        break;

                    case MessageType.ChatMessage:
                        HandleChatMessage(message);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки сообщения: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
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
                OrientationButton.IsEnabled = false;
                ReadyButton.IsEnabled = false;
                SurrenderButton.IsEnabled = true;
            }
            else
            {
                RandomPlaceButton.IsEnabled = true;
                ClearBoardButton.IsEnabled = true;
                OrientationButton.IsEnabled = true;
                SurrenderButton.IsEnabled = false;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
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
