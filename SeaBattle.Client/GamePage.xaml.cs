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
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private string _playerId;
        private string _playerName;
        private string _roomId;
        private bool _isConnected;
        private CancellationTokenSource _cancellationTokenSource;

        private GameBoard _myBoard;
        private GameBoard _enemyBoard;

        public GamePage(TcpClient tcpClient, NetworkStream stream, string playerId, string playerName, string roomId)
        {
            InitializeComponent();

            _tcpClient = tcpClient;
            _stream = stream;
            _playerId = playerId;
            _playerName = playerName;
            _roomId = roomId;
            _isConnected = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Инициализация полей
            _myBoard = new GameBoard();
            _enemyBoard = new GameBoard();

            MyBoardControl.GameBoard = _myBoard;
            EnemyBoardControl.GameBoard = _enemyBoard;

            PlayerNameText.Text = _playerName;
            EnemyTitleText.Text = "Поле противника";

            UpdateShipsStatus();

            // Запускаем прослушивание
            Task.Run(() => ListenToServer(_cancellationTokenSource.Token));
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
            // Здесь будет обработка сообщений от сервера
        }

        // Расстановка кораблей
        private void MyBoardCellClicked(object sender, CellClickEventArgs e)
        {
            // Ищем неразмещенный корабль
            var shipToPlace = _myBoard.Ships.FirstOrDefault(s => !s.IsPlaced);
            if (shipToPlace == null)
            {
                MessageBox.Show("Все корабли уже размещены!", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Пробуем разместить горизонтально
            if (_myBoard.CanPlaceShip(e.X, e.Y, shipToPlace.Size, true))
            {
                _myBoard.PlaceShip(shipToPlace, e.X, e.Y, true);
                MyBoardControl.UpdateBoard();
                UpdateShipsStatus();
            }
            // Пробуем разместить вертикально
            else if (_myBoard.CanPlaceShip(e.X, e.Y, shipToPlace.Size, false))
            {
                _myBoard.PlaceShip(shipToPlace, e.X, e.Y, false);
                MyBoardControl.UpdateBoard();
                UpdateShipsStatus();
            }
            else
            {
                MessageBox.Show("Нельзя разместить корабль здесь!", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnemyBoardCellClicked(object sender, CellClickEventArgs e)
        {
            // Здесь будет логика атаки
        }

        private void RandomPlaceButton_Click(object sender, RoutedEventArgs e)
        {
            _myBoard = new GameBoard();
            _myBoard.RandomPlacement();
            MyBoardControl.GameBoard = _myBoard;
            MyBoardControl.UpdateBoard();
            UpdateShipsStatus();
        }

        private void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
            _myBoard = new GameBoard();
            MyBoardControl.GameBoard = _myBoard;
            MyBoardControl.UpdateBoard();
            UpdateShipsStatus();
            ReadyButton.IsEnabled = false;
        }

        private void ReadyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_myBoard.IsReady)
            {
                MessageBox.Show("Разместите все корабли!", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ReadyButton.IsEnabled = false;
            RandomPlaceButton.IsEnabled = false;
            ClearBoardButton.IsEnabled = false;
            StatusText.Text = "Ожидание противника...";
            StatusText.Foreground = Brushes.Orange;
        }

        private void SurrenderButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Вы действительно хотите сдаться?", "Подтверждение",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                ExitGame();
            }
        }

        private void ExitGameButton_Click(object sender, RoutedEventArgs e)
        {
            ExitGame();
        }

        private void ExitGame()
        {
            var mainWindow = new MainWindow();
            var currentWindow = (MainWindow)Window.GetWindow(this);
            currentWindow.Content = mainWindow.Content;
        }

        private void UpdateShipsStatus()
        {
            int placed = _myBoard.Ships.Count(s => s.IsPlaced);
            int total = _myBoard.Ships.Count;
            ShipsStatusText.Text = $"{placed}/{total} размещено";

            if (_myBoard.IsReady)
            {
                ReadyButton.IsEnabled = true;
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

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isConnected = false;
            _cancellationTokenSource?.Cancel();
        }
    }
}