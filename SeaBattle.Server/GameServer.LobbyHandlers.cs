using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;

namespace SeaBattle.Server
{
    public partial class GameServer
    {
        private NetworkMessage HandleConnect(NetworkMessage message, string connectionId, NetworkStream stream)
        {
            var data = message.Data.ToObject<ConnectData>();

            var existingPlayer = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);

            ConnectedPlayer player;
            string playerId;

            if (existingPlayer != null)
            {
                playerId = existingPlayer.Id;
                Console.WriteLine($"Игрок {data.PlayerName} переподключается с ID: {playerId}");

                existingPlayer.Name = data.PlayerName;
                existingPlayer.Status = PlayerStatus.Online;
                existingPlayer.LastSeen = DateTime.UtcNow;

                player = existingPlayer;
            }
            else
            {
                GameRoom reconnectRoom = null;
                ConnectedPlayer disconnectedPlayer = null;
                foreach (var r in _lobbyManager.GetAllRooms())
                {
                    if (r.Status != GameRoomStatus.InGame) continue;
                    if (r.Creator != null && r.Creator.Name == data.PlayerName && !_playerStreams.ContainsKey(r.Creator.Id))
                    {
                        reconnectRoom = r;
                        disconnectedPlayer = r.Creator;
                        break;
                    }
                    if (r.Player2 != null && r.Player2.Name == data.PlayerName && !_playerStreams.ContainsKey(r.Player2.Id))
                    {
                        reconnectRoom = r;
                        disconnectedPlayer = r.Player2;
                        break;
                    }
                }

                if (reconnectRoom != null && disconnectedPlayer != null)
                {
                    disconnectedPlayer.ConnectionId = connectionId;
                    disconnectedPlayer.Status = PlayerStatus.InGame;
                    disconnectedPlayer.LastSeen = DateTime.UtcNow;
                    playerId = disconnectedPlayer.Id;

                    return new NetworkMessage
                    {
                        Type = MessageType.ConnectResponse,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(new ConnectResponseData
                        {
                            PlayerId = playerId,
                            Message = $"Добро пожаловать, {data.PlayerName}! У вас есть незавершённая игра.",
                            Success = true,
                            PendingReconnectRoomId = reconnectRoom.Id
                        })
                    };
                }

                playerId = Guid.NewGuid().ToString();
                Console.WriteLine($"Новый игрок {data.PlayerName} с ID: {playerId}");

                player = new ConnectedPlayer
                {
                    Id = playerId,
                    ConnectionId = connectionId,
                    Name = data.PlayerName,
                    Status = PlayerStatus.Online,
                    ConnectedAt = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                };
            }

            _players[playerId] = player;
            _playerStreams[playerId] = stream;

            return new NetworkMessage
            {
                Type = MessageType.ConnectResponse,
                SenderId = "SERVER",
                Data = JObject.FromObject(new ConnectResponseData
                {
                    PlayerId = playerId,
                    Message = $"Добро пожаловать, {data.PlayerName}!",
                    Success = true
                })
            };
        }

        private async Task<NetworkMessage> HandleChatMessage(NetworkMessage message, string connectionId)
        {
            var data = message.Data?.ToObject<ChatMessageData>();
            if (data == null || string.IsNullOrWhiteSpace(data.Message))
                return null;

            var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null) return null;

            var room = _lobbyManager.GetPlayerRoom(player.Id);
            if (room == null) return null;

            var opponent = room.GetOpponent(player.Id);
            if (opponent == null || !_playerStreams.TryGetValue(opponent.Id, out var opponentStream))
                return null;

            var chatPayload = new ChatMessageData
            {
                Message = data.Message.Trim(),
                SenderName = player.Name
            };
            var chatMsg = new NetworkMessage
            {
                Type = MessageType.ChatMessage,
                SenderId = player.Id,
                Data = JObject.FromObject(chatPayload)
            };
            await SendMessageAsync(opponentStream, chatMsg);
            return null;
        }

        private async Task<NetworkMessage> HandleCreateRoom(NetworkMessage message, string connectionId)
        {
            try
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

                string roomName = message.Data["roomName"]?.ToString() ?? "Новая комната";
                var room = _lobbyManager.CreateRoom(roomName, player);

                if (_playerStreams.TryGetValue(player.Id, out var stream))
                {
                    var roomCreatedMsg = new NetworkMessage
                    {
                        Type = MessageType.RoomCreated,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(new
                        {
                            RoomId = room.Id,
                            RoomName = room.Name,
                            Message = $"Комната '{room.Name}' создана"
                        })
                    };
                    await SendMessageAsync(stream, roomCreatedMsg);
                }

                BroadcastRoomsList();
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка создания комнаты: {ex.Message}");
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = $"Ошибка: {ex.Message}" })
                };
            }
        }

        private async Task<NetworkMessage> HandleJoinRoom(NetworkMessage message, string connectionId)
        {
            try
            {
                string roomId = message.Data["roomId"]?.ToString();
                var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);

                if (player == null || string.IsNullOrEmpty(roomId))
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Игрок или комната не найдены" })
                    };
                }

                var playerCurrentRoom = _lobbyManager.GetPlayerRoom(player.Id);
                if (playerCurrentRoom != null)
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Вы уже в другой комнате" })
                    };
                }

                if (_lobbyManager.JoinRoom(roomId, player))
                {
                    var room = _lobbyManager.GetPlayerRoom(player.Id);

                    BroadcastRoomsList();

                    if (room?.Creator != null && room.Creator.Id != player.Id)
                    {
                        if (_playerStreams.TryGetValue(room.Creator.Id, out var creatorStream))
                        {
                            var joinNotification = new NetworkMessage
                            {
                                Type = MessageType.PlayerJoinedRoom,
                                SenderId = "SERVER",
                                Data = JObject.FromObject(new
                                {
                                    PlayerId = player.Id,
                                    PlayerName = player.Name,
                                    RoomId = room.Id,
                                    Message = $"Игрок {player.Name} присоединился"
                                })
                            };
                            await SendMessageAsync(creatorStream, joinNotification);
                        }
                    }

                    var joinedMessage = new NetworkMessage
                    {
                        Type = MessageType.JoinedRoom,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(new
                        {
                            RoomId = room.Id,
                            RoomName = room.Name,
                            Message = $"Вы присоединились к комнате '{room.Name}'"
                        })
                    };

                    if (_playerStreams.TryGetValue(player.Id, out var playerStream))
                    {
                        await SendMessageAsync(playerStream, joinedMessage);
                    }

                    if (room.IsFull)
                    {
                        if (_lobbyManager.StartGame(room.Id))
                        {
                            var gameSession = new GameSession
                            {
                                RoomId = room.Id,
                                Player1 = room.Creator,
                                Player2 = room.Player2,
                                Status = GameSessionStatus.PlacingShips
                            };
                            _gameSessions[room.Id] = gameSession;

                            var startGameMessageTemplate = new GameStartData
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
                            };

                            if (_playerStreams.TryGetValue(room.Creator.Id, out var creatorStream))
                            {
                                var creatorStartMessage = new NetworkMessage
                                {
                                    Type = MessageType.StartGame,
                                    SenderId = "SERVER",
                                    Data = JObject.FromObject(new GameStartData
                                    {
                                        RoomId = startGameMessageTemplate.RoomId,
                                        Player1 = startGameMessageTemplate.Player1,
                                        Player2 = startGameMessageTemplate.Player2,
                                        YourPlayerId = room.Creator.Id
                                    })
                                };
                                await SendMessageAsync(creatorStream, creatorStartMessage);
                            }

                            if (_playerStreams.TryGetValue(room.Player2.Id, out var player2Stream))
                            {
                                var player2StartMessage = new NetworkMessage
                                {
                                    Type = MessageType.StartGame,
                                    SenderId = "SERVER",
                                    Data = JObject.FromObject(new GameStartData
                                    {
                                        RoomId = startGameMessageTemplate.RoomId,
                                        Player1 = startGameMessageTemplate.Player1,
                                        Player2 = startGameMessageTemplate.Player2,
                                        YourPlayerId = room.Player2.Id
                                    })
                                };
                                await SendMessageAsync(player2Stream, player2StartMessage);
                            }
                        }

                        BroadcastRoomsList();
                    }

                    return null;
                }
                else
                {
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Не удалось присоединиться к комнате" })
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка присоединения: {ex.Message}");
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = $"Ошибка: {ex.Message}" })
                };
            }
        }

        private NetworkMessage HandleGetRooms(string playerId)
        {
            var rooms = _lobbyManager.GetRoomsForPlayer(playerId);

            return new NetworkMessage
            {
                Type = MessageType.RoomsList,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    rooms = rooms,
                    count = rooms.Count
                })
            };
        }

        private async Task<NetworkMessage> HandleLeaveRoom(NetworkMessage message, string connectionId)
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
            bool canReconnect = false;
            string reconnectRoomId = null;

            if (room != null)
            {
                var opponent = room.GetOpponent(player.Id);
                bool gameInProgress = _gameSessions.TryGetValue(room.Id, out var session) && session.Status == GameSessionStatus.InProgress;

                if (gameInProgress)
                {
                    canReconnect = true;
                    reconnectRoomId = room.Id;

                    if (opponent != null && _playerStreams.TryGetValue(opponent.Id, out var opponentStream))
                    {
                        await SendMessageAsync(opponentStream, new NetworkMessage
                        {
                            Type = MessageType.OpponentDisconnected,
                            SenderId = "SERVER",
                            Data = JObject.FromObject(new { PlayerName = player.Name })
                        });
                    }
                }
                else
                {
                    if (opponent != null && _playerStreams.TryGetValue(opponent.Id, out var opponentStream))
                    {
                        var leaveNotification = new NetworkMessage
                        {
                            Type = MessageType.PlayerLeftRoom,
                            SenderId = "SERVER",
                            Data = JObject.FromObject(new
                            {
                                PlayerId = player.Id,
                                PlayerName = player.Name,
                                RoomId = room.Id,
                                Message = "Игрок покинул комнату"
                            })
                        };
                        await SendMessageAsync(opponentStream, leaveNotification);
                    }

                    string roomIdToRemove = room.Id;
                    _lobbyManager.RemoveRoom(roomIdToRemove);
                    _gameSessions.TryRemove(roomIdToRemove, out _);
                    Console.WriteLine($"Игрок {player.Name} покинул комнату; комната удалена");
                }

                BroadcastRoomsList();
            }
            else
            {
                BroadcastRoomsList();
            }

            var responseData = new
            {
                Message = canReconnect ? "Вы вышли из игры. Можете переподключиться." : "Вы покинули комнату",
                PendingReconnectRoomId = canReconnect ? reconnectRoomId : (string)null
            };

            return new NetworkMessage
            {
                Type = MessageType.JoinedRoom,
                SenderId = "SERVER",
                Data = JObject.FromObject(responseData)
            };
        }
    }
}
