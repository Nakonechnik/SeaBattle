using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class GamePage
    {
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

            bool horizontal = _placeShipHorizontal;
            if (_myBoard.CanPlaceShip(e.X, e.Y, shipToPlace.Size, horizontal))
            {
                _myBoard.PlaceShip(shipToPlace, e.X, e.Y, horizontal);
                MyBoardControl.UpdateBoard();
                UpdateShipsStatus();
            }
            else
            {
                MessageBox.Show(
                    "Корабль выходит за границы поля или пересекается с другими кораблями.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

        private void StartTurnTimer(int secondsLeft)
        {
            _secondsLeft = secondsLeft;
            TimerBorder.Visibility = Visibility.Visible;
            UpdateTimerText();
            _turnTimer.Stop();
            _turnTimer.Start();
        }

        private void StopTurnTimer()
        {
            _turnTimer.Stop();
            TimerBorder.Visibility = Visibility.Collapsed;
        }

        private void TurnTimer_Tick(object sender, EventArgs e)
        {
            _secondsLeft--;
            UpdateTimerText();
            if (_secondsLeft <= 0)
            {
                _turnTimer.Stop();
                TimerBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateTimerText()
        {
            int min = Math.Max(0, _secondsLeft) / 60;
            int sec = Math.Max(0, _secondsLeft) % 60;
            TimerText.Text = $"Осталось: {min}:{sec:D2}";
            TimerText.Foreground = _secondsLeft <= 30 ? Brushes.OrangeRed : Brushes.White;
        }

        private void OrientationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameStarted) return;
            _placeShipHorizontal = !_placeShipHorizontal;
            OrientationButton.Content = _placeShipHorizontal ? "Горизонтально" : "Вертикально";
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
                OrientationButton.IsEnabled = false;
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
            if (_gameOverHandled)
                return;
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
    }
}
