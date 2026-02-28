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
        private void HandleGameReady(NetworkMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                var playerData = message.Data.ToObject<PlayerInfo>();
                string playerName = playerData?.Name ?? "Противник";
                StatusText.Text = $"{playerName} готов! Ожидание начала игры...";
                StatusText.Foreground = Brushes.LightGreen;
                GamePhaseText.Text = "Ожидание противника";
            });
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

                if (gameState.MyBoard != null)
                    _myBoard = gameState.MyBoard;
                if (gameState.EnemyBoard != null)
                    _enemyBoard = gameState.EnemyBoard;

                Dispatcher.Invoke(() =>
                {
                    MyBoardControl.GameBoard = _myBoard;
                    EnemyBoardControl.GameBoard = _enemyBoard;
                    MyBoardControl.UpdateBoard();
                    EnemyBoardControl.UpdateBoard();
                    UpdateShipsStatus();

                    EnemyTitleText.Text = $"ПОЛЕ {_enemyPlayerName?.ToUpper()}";

                    IsEnemyBoardEnabled = _isMyTurn;
                    StatusText.Text = _isMyTurn ? "Ваш ход!" : $"Ход {_enemyPlayerName}";
                    StatusText.Foreground = _isMyTurn ? Brushes.LightGreen : Brushes.Orange;
                    GamePhaseText.Text = "Идет бой";
                    SurrenderButton.IsEnabled = true;

                    RandomPlaceButton.IsEnabled = false;
                    ClearBoardButton.IsEnabled = false;
                    OrientationButton.IsEnabled = false;
                    ReadyButton.IsEnabled = false;

                    if (_isMyTurn)
                    {
                        StartTurnTimer(gameState.TimeLeft > 0 ? gameState.TimeLeft : GameConstants.TurnTimeSeconds);
                        TurnStatusText.Text = "Выберите цель для атаки";
                    }
                    else
                    {
                        StopTurnTimer();
                        TurnStatusText.Text = $"Ожидание хода {_enemyPlayerName}";
                    }
                });
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
                if (result.X < 0 || result.X >= 10 || result.Y < 0 || result.Y >= 10) return;

                bool iAmAttacker = message.SenderId == App.PlayerId;

                if (iAmAttacker)
                {
                    if (result.ChangedCells != null && result.ChangedCells.Count > 0)
                    {
                        foreach (var cell in result.ChangedCells)
                        {
                            if (cell.X >= 0 && cell.X < 10 && cell.Y >= 0 && cell.Y < 10)
                            {
                                if (result.ShipCells != null && result.ShipCells.Any(c => c.X == cell.X && c.Y == cell.Y))
                                    _enemyBoard.Cells[cell.X, cell.Y] = CellState.Destroyed;
                                else if (cell.X == result.X && cell.Y == result.Y)
                                    _enemyBoard.Cells[cell.X, cell.Y] = result.IsHit
                                        ? (result.IsDestroyed ? CellState.Destroyed : CellState.Hit)
                                        : CellState.Miss;
                                else
                                    _enemyBoard.Cells[cell.X, cell.Y] = CellState.Miss;
                                _enemyBoard.VisibleCells[cell.X, cell.Y] = true;
                            }
                        }
                    }
                    else
                    {
                        _enemyBoard.Cells[result.X, result.Y] = result.IsHit
                            ? (result.IsDestroyed ? CellState.Destroyed : CellState.Hit)
                            : CellState.Miss;
                        _enemyBoard.VisibleCells[result.X, result.Y] = true;
                    }

                    EnemyBoardControl.UpdateBoard();

                    string hitMessage = result.IsDestroyed
                        ? $"Корабль уничтожен! (размер: {result.ShipSize})"
                        : (result.IsHit ? "Попадание!" : "Промах");
                    TurnStatusText.Text = hitMessage;
                    TurnStatusText.Foreground = result.IsHit ? Brushes.LightGreen : Brushes.Orange;

                    if (result.IsHit && !result.IsGameOver)
                    {
                        IsEnemyBoardEnabled = true;
                        TurnStatusText.Text = result.IsDestroyed
                            ? $"Корабль уничтожен! (размер: {result.ShipSize}) — стреляйте ещё"
                            : "Попадание! Стреляйте ещё раз";
                        Dispatcher.Invoke(() => StartTurnTimer(GameConstants.TurnTimeSeconds));
                    }
                }
                else
                {
                    if (result.ChangedCells != null && result.ChangedCells.Count > 0)
                    {
                        foreach (var cell in result.ChangedCells)
                        {
                            if (cell.X >= 0 && cell.X < 10 && cell.Y >= 0 && cell.Y < 10)
                            {
                                if (result.ShipCells != null && result.ShipCells.Any(c => c.X == cell.X && c.Y == cell.Y))
                                    _myBoard.Cells[cell.X, cell.Y] = CellState.Destroyed;
                                else if (cell.X == result.X && cell.Y == result.Y)
                                    _myBoard.Cells[cell.X, cell.Y] = result.IsHit
                                        ? (result.IsDestroyed ? CellState.Destroyed : CellState.Hit)
                                        : CellState.Miss;
                                else
                                    _myBoard.Cells[cell.X, cell.Y] = CellState.Miss;
                            }
                        }
                    }
                    else
                    {
                        _myBoard.Cells[result.X, result.Y] = result.IsHit
                            ? (result.IsDestroyed ? CellState.Destroyed : CellState.Hit)
                            : CellState.Miss;
                    }

                    MyBoardControl.UpdateBoard();

                    TurnStatusText.Text = result.IsHit
                        ? (result.IsDestroyed ? "Противник уничтожил ваш корабль!" : "Противник попал!")
                        : "Противник промахнулся.";
                    TurnStatusText.Foreground = result.IsHit ? Brushes.LightCoral : Brushes.Orange;
                }

                if (result.IsGameOver)
                    ApplyGameOverUI(result.WinnerId == App.PlayerId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки результата атаки: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Отключает доски и кнопки, показывает победу/поражение, возвращает в лобби.
        private void ApplyGameOverUI(bool isWinner, string messageForBox = null)
        {
            if (_gameOverHandled) return;
            _gameOverHandled = true;

            StopTurnTimer();
            IsEnemyBoardEnabled = false;
            MyBoardControl.IsEnabled = false;
            SurrenderButton.IsEnabled = false;
            ExitGameButton.IsEnabled = false;
            RandomPlaceButton.IsEnabled = false;
            ClearBoardButton.IsEnabled = false;
            OrientationButton.IsEnabled = false;
            ReadyButton.IsEnabled = false;
            StatusText.Text = isWinner ? "Победа!" : "Поражение";
            StatusText.Foreground = isWinner ? Brushes.Gold : Brushes.Red;
            GamePhaseText.Text = "Игра окончена";
            TurnStatusText.Text = isWinner ? "Все корабли противника уничтожены!" : "Все ваши корабли уничтожены.";

            string msg = messageForBox ?? (isWinner
                ? "Поздравляем! Вы победили! Все корабли противника уничтожены!"
                : "Вы проиграли. Все ваши корабли уничтожены.");
            MessageBox.Show(msg, "Игра окончена",
                MessageBoxButton.OK, isWinner ? MessageBoxImage.Information : MessageBoxImage.Exclamation);

            Task.Run(async () =>
            {
                await Task.Delay(2000);
                await Dispatcher.InvokeAsync(() => ReturnToLobby());
            });
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
                    StartTurnTimer(data.TimeLeft > 0 ? data.TimeLeft : GameConstants.TurnTimeSeconds);
                }
                else
                {
                    StatusText.Text = $"Ход {_enemyPlayerName}...";
                    StatusText.Foreground = Brushes.Orange;
                    TurnStatusText.Text = $"Ожидание хода {_enemyPlayerName}";
                    TurnStatusText.Foreground = Brushes.Orange;
                    StopTurnTimer();
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
                if (_gameOverHandled) return;

                var data = message.Data.ToObject<GameOverData>();
                bool isWinner = data.WinnerId == App.PlayerId;

                string gameOverMessage;
                if (data.IsTimeout)
                    gameOverMessage = isWinner ? "Противник не успел сделать ход. Вы победили!" : "Время вышло. Вы проиграли.";
                else if (data.IsSurrender)
                    gameOverMessage = isWinner ? "Противник сдался. Вы победили!" : "Вы сдались. Игра окончена.";
                else
                    gameOverMessage = isWinner ? "Поздравляем! Вы победили! Все корабли противника уничтожены!" : $"Вы проиграли. Все ваши корабли уничтожены. Победитель: {data.WinnerName}";

                ApplyGameOverUI(isWinner, gameOverMessage);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки конца игры: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                ReturnToLobby();
            }
        }

        private void HandlePlayerLeftRoom(NetworkMessage message)
        {
            ExitGameButton.IsEnabled = false;
            StopTurnTimer();
            string playerName = message.Data?["PlayerName"]?.ToString() ?? "Противник";
            MessageBox.Show($"{playerName} покинул игру!", "Информация",
                          MessageBoxButton.OK, MessageBoxImage.Information);

            ReturnToLobby();
        }

        private void HandleOpponentDisconnected(NetworkMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                string playerName = message.Data?["PlayerName"]?.ToString() ?? "Противник";
                StatusText.Text = $"{playerName} отключился";
                StatusText.Foreground = Brushes.Orange;
                TurnStatusText.Text = "Противник может переподключиться до конца своего следующего хода. Ожидайте.";
            });
        }

        private void HandleOpponentReconnected(NetworkMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                string playerName = message.Data?["PlayerName"]?.ToString() ?? "Противник";
                StatusText.Text = _isMyTurn ? "Ваш ход!" : $"Ход {playerName}";
                StatusText.Foreground = _isMyTurn ? Brushes.LightGreen : Brushes.Orange;
                TurnStatusText.Text = $"{playerName} переподключился.";
            });
        }

        private async Task SendReconnectToGameAsync()
        {
            try
            {
                var message = new NetworkMessage
                {
                    Type = MessageType.ReconnectToGame,
                    SenderId = App.PlayerId,
                    Data = JObject.FromObject(new { RoomId = _roomId })
                };
                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка переподключения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ReturnToLobby();
            }
        }

        private void HandleChatMessage(NetworkMessage message)
        {
            var data = message.Data?.ToObject<ChatMessageData>();
            if (data == null || string.IsNullOrEmpty(data.Message)) return;
            string line = $"{data.SenderName}: {data.Message}";
            Dispatcher.Invoke(() =>
            {
                ChatListBox.Items.Add(line);
                if (ChatListBox.Items.Count > 0)
                    ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
            });
        }
    }
}
