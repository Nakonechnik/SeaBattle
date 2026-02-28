using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;

namespace SeaBattle.Server
{
    public partial class GameServer
    {
        private async Task<NetworkMessage> HandleStartGame(NetworkMessage message, string connectionId)
        {
            try
            {
                var data = message.Data.ToObject<JoinRoomData>();

                var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player == null)
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Игрок не найден" })
                    };
                }

                var room = _lobbyManager.GetPlayerRoom(player.Id);
                if (room == null)
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Комната не найдена" })
                    };
                }

                if (room.Creator?.Id != player.Id)
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Только создатель комнаты может начать игру" })
                    };
                }

                if (!room.IsFull)
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Ожидание второго игрока" })
                    };
                }

                if (_lobbyManager.StartGame(data.RoomId))
                {
                    var gameSession = new GameSession
                    {
                        RoomId = room.Id,
                        Player1 = room.Creator,
                        Player2 = room.Player2,
                        Status = GameSessionStatus.PlacingShips
                    };
                    _gameSessions[room.Id] = gameSession;

                    var startGameMessage = new NetworkMessage
                    {
                        Type = MessageType.StartGame,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(new GameStartData
                        {
                            RoomId = room.Id,
                            Player1 = new PlayerInfo
                            {
                                Id = room.Creator.Id,
                                Name = room.Creator.Name,
                                Status = "InGame"
                            },
                            Player2 = new PlayerInfo
                            {
                                Id = room.Player2.Id,
                                Name = room.Player2.Name,
                                Status = "InGame"
                            }
                        })
                    };

                    if (_playerStreams.TryGetValue(room.Creator.Id, out var creatorStream))
                    {
                        await SendMessageAsync(creatorStream, startGameMessage);
                    }

                    if (room.Player2 != null && _playerStreams.TryGetValue(room.Player2.Id, out var player2Stream))
                    {
                        await SendMessageAsync(player2Stream, startGameMessage);
                    }

                    BroadcastRoomsList();
                    return null;
                }
                else
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Не удалось начать игру" })
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в HandleStartGame: {ex.Message}");
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = $"Ошибка: {ex.Message}" })
                };
            }
        }

        private async Task<NetworkMessage> HandleGameReadyAsync(NetworkMessage message, string connectionId)
        {
            var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игрок не найден" })
                };
            }

            var room = _lobbyManager.GetPlayerRoom(player.Id);
            if (room == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Комната не найдена" })
                };
            }

            var gameSession = _gameSessions.GetOrAdd(room.Id, _ => new GameSession());
            gameSession.SetPlayerReady(player.Id, message.Data?["Board"]?.ToObject<GameBoard>());

            var opponent = room.GetOpponent(player.Id);
            if (opponent != null && _playerStreams.TryGetValue(opponent.Id, out var opponentStream))
            {
                var readyNotification = new NetworkMessage
                {
                    Type = MessageType.GameReady,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(new
                    {
                        PlayerId = player.Id,
                        PlayerName = player.Name
                    })
                };
                await SendMessageAsync(opponentStream, readyNotification);
            }

            if (gameSession.AreBothPlayersReady())
            {
                gameSession.Status = GameSessionStatus.InProgress;
                gameSession.CurrentTurnPlayerId = new Random().Next(2) == 0 ? room.Creator.Id : room.Player2.Id;

                await SendGameStateToBothPlayers(gameSession, room);
                StartTurnTimer(room.Id);
            }

            return null;
        }

        private async Task<NetworkMessage> HandleAttack(NetworkMessage message, string connectionId)
        {
            var attackData = message.Data.ToObject<AttackData>();

            var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игрок не найден" })
                };
            }

            var room = _lobbyManager.GetPlayerRoom(player.Id);
            if (room == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Комната не найдена" })
                };
            }

            _gameSessions.TryGetValue(room.Id, out var gameSession);
            if (gameSession == null || gameSession.Status != GameSessionStatus.InProgress)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игра не начата или уже закончена" })
                };
            }

            if (gameSession.CurrentTurnPlayerId != player.Id)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Сейчас не ваш ход" })
                };
            }

            var opponent = room.GetOpponent(player.Id);
            var opponentBoard = gameSession.GetPlayerBoard(opponent.Id);

            if (opponentBoard == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Доска противника не найдена" })
                };
            }

            var attackResult = opponentBoard.Attack(attackData.X, attackData.Y);
            attackResult.AttackerId = player.Id;

            if (!attackResult.IsValid)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = attackResult.Message })
                };
            }

            bool gameOver = opponentBoard.HasNoShipCellsLeft();
            if (gameOver)
            {
                attackResult.IsGameOver = true;
                attackResult.WinnerId = player.Id;
                gameSession.Status = GameSessionStatus.Finished;

                if (_playerStreams.TryGetValue(player.Id, out var winnerStreamForResult))
                {
                    var resultMessage = new NetworkMessage
                    {
                        Type = MessageType.AttackResult,
                        SenderId = player.Id,
                        Data = JObject.FromObject(attackResult)
                    };
                    await SendMessageAsync(winnerStreamForResult, resultMessage);
                }
                if (_playerStreams.TryGetValue(opponent.Id, out var loserStreamForResult))
                {
                    var defenderResultMessage = new NetworkMessage
                    {
                        Type = MessageType.AttackResult,
                        SenderId = player.Id,
                        Data = JObject.FromObject(attackResult)
                    };
                    await SendMessageAsync(loserStreamForResult, defenderResultMessage);
                }

                var gameOverData = new GameOverData
                {
                    WinnerId = player.Id,
                    WinnerName = player.Name,
                    LoserId = opponent.Id,
                    LoserName = opponent.Name,
                    IsSurrender = false
                };

                if (_playerStreams.TryGetValue(player.Id, out var winnerStream))
                {
                    await SendMessageAsync(winnerStream, new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    });
                }

                if (_playerStreams.TryGetValue(opponent.Id, out var loserStream))
                {
                    await SendMessageAsync(loserStream, new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    });
                }

                CancelTurnTimer(room.Id);
                _lobbyManager.RemoveRoom(room.Id);
                _gameSessions.TryRemove(room.Id, out _);
                BroadcastRoomsList();

                return null;
            }

            if (_playerStreams.TryGetValue(player.Id, out var attackerStream))
            {
                await SendMessageAsync(attackerStream, new NetworkMessage
                {
                    Type = MessageType.AttackResult,
                    SenderId = player.Id,
                    Data = JObject.FromObject(attackResult)
                });
            }

            if (_playerStreams.TryGetValue(opponent.Id, out var defenderStream))
            {
                await SendMessageAsync(defenderStream, new NetworkMessage
                {
                    Type = MessageType.AttackResult,
                    SenderId = player.Id,
                    Data = JObject.FromObject(attackResult)
                });
            }

            if (opponentBoard.HasNoShipCellsLeft())
            {
                gameSession.Status = GameSessionStatus.Finished;
                CancelTurnTimer(room.Id);
                var gameOverData = new GameOverData
                {
                    WinnerId = player.Id,
                    WinnerName = player.Name,
                    LoserId = opponent.Id,
                    LoserName = opponent.Name,
                    IsSurrender = false
                };
                if (_playerStreams.TryGetValue(player.Id, out var winnerStreamGo))
                {
                    await SendMessageAsync(winnerStreamGo, new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    });
                }
                if (_playerStreams.TryGetValue(opponent.Id, out var loserStreamGo))
                {
                    await SendMessageAsync(loserStreamGo, new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    });
                }
                _lobbyManager.RemoveRoom(room.Id);
                _gameSessions.TryRemove(room.Id, out _);
                BroadcastRoomsList();
                return null;
            }

            if (!attackResult.IsHit)
            {
                gameSession.CurrentTurnPlayerId = opponent.Id;

                var turnChangeMessage = new NetworkMessage
                {
                    Type = MessageType.TurnChanged,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(new TurnChangeData
                    {
                        NextPlayerId = opponent.Id,
                        PreviousPlayerId = player.Id,
                        TimeLeft = GameConstants.TurnTimeSeconds
                    })
                };

                if (_playerStreams.TryGetValue(player.Id, out var playerStream))
                {
                    await SendMessageAsync(playerStream, turnChangeMessage);
                }

                if (_playerStreams.TryGetValue(opponent.Id, out var oppStream))
                {
                    await SendMessageAsync(oppStream, turnChangeMessage);
                }

                StartTurnTimer(room.Id);
            }
            else
            {
                StartTurnTimer(room.Id);
            }

            return null;
        }

        private async Task<NetworkMessage> HandleGameOver(NetworkMessage message, string connectionId)
        {
            var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игрок не найден" })
                };
            }

            var room = _lobbyManager.GetPlayerRoom(player.Id);
            if (room != null)
            {
                _gameSessions.TryGetValue(room.Id, out var gameSession);
                if (gameSession != null)
                {
                    gameSession.Status = GameSessionStatus.Finished;
                }

                var opponent = room.GetOpponent(player.Id);
                if (opponent != null && _playerStreams.TryGetValue(opponent.Id, out var opponentStream))
                {
                    var gameOverData = new GameOverData
                    {
                        WinnerId = opponent.Id,
                        WinnerName = opponent.Name,
                        LoserId = player.Id,
                        LoserName = player.Name,
                        IsSurrender = true
                    };
                    var gameOverMessage = new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    };
                    await SendMessageAsync(opponentStream, gameOverMessage);
                }

                CancelTurnTimer(room.Id);
                _lobbyManager.RemoveRoom(room.Id);
                _gameSessions.TryRemove(room.Id, out _);

                BroadcastRoomsList();
            }

            return null;
        }

        private async Task<NetworkMessage> HandleReconnectToGame(NetworkMessage message, string connectionId)
        {
            var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игрок не найден" })
                };
            }

            var roomId = message.Data?["RoomId"]?.ToString() ?? message.Data?["roomId"]?.ToString();
            if (string.IsNullOrEmpty(roomId))
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Не указана комната" })
                };
            }

            var room = _lobbyManager.GetRoom(roomId);
            if (room == null || !room.ContainsPlayer(player.Id))
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Комната не найдена или вы не участник этой игры" })
                };
            }

            if (!_gameSessions.TryGetValue(roomId, out var gameSession) || gameSession.Status != GameSessionStatus.InProgress)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игра в этой комнате уже завершена" })
                };
            }

            var player1Board = gameSession.GetPlayerBoard(room.Creator.Id);
            var player2Board = gameSession.GetPlayerBoard(room.Player2.Id);
            if (player1Board == null || player2Board == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Ошибка состояния игры" })
                };
            }

            int timeLeft = GetTimeLeftForRoom(roomId);
            GameState stateForReconnecting;
            if (room.Creator.Id == player.Id)
            {
                stateForReconnecting = new GameState
                {
                    RoomId = room.Id,
                    MyBoard = player1Board,
                    EnemyBoard = player2Board,
                    CurrentTurnPlayerId = gameSession.CurrentTurnPlayerId,
                    MyPlayerId = room.Creator.Id,
                    EnemyPlayerId = room.Player2.Id,
                    EnemyPlayerName = room.Player2.Name,
                    Phase = "InGame",
                    TimeLeft = timeLeft
                };
            }
            else
            {
                stateForReconnecting = new GameState
                {
                    RoomId = room.Id,
                    MyBoard = player2Board,
                    EnemyBoard = player1Board,
                    CurrentTurnPlayerId = gameSession.CurrentTurnPlayerId,
                    MyPlayerId = room.Player2.Id,
                    EnemyPlayerId = room.Creator.Id,
                    EnemyPlayerName = room.Creator.Name,
                    Phase = "InGame",
                    TimeLeft = timeLeft
                };
            }

            if (_playerStreams.TryGetValue(player.Id, out var playerStream))
            {
                await SendMessageAsync(playerStream, new NetworkMessage
                {
                    Type = MessageType.GameState,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(stateForReconnecting)
                });
            }

            var opponent = room.GetOpponent(player.Id);
            if (opponent != null && _playerStreams.TryGetValue(opponent.Id, out var opponentStream))
            {
                await SendMessageAsync(opponentStream, new NetworkMessage
                {
                    Type = MessageType.OpponentReconnected,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(new { PlayerName = player.Name })
                });
            }

            return null;
        }
    }
}
