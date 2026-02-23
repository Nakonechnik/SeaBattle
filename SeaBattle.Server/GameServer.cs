using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;
using System.Threading;

namespace SeaBattle.Server
{
    // Класс для игровой сессии
    public class GameSession
    {
        public string RoomId { get; set; }
        public ConnectedPlayer Player1 { get; set; }
        public ConnectedPlayer Player2 { get; set; }
        public GameBoard Player1Board { get; set; }
        public GameBoard Player2Board { get; set; }
        public bool Player1Ready { get; set; }
        public bool Player2Ready { get; set; }
        public GameSessionStatus Status { get; set; }
        public string CurrentTurnPlayerId { get; set; }

        public void SetPlayerReady(string playerId, GameBoard board)
        {
            if (Player1?.Id == playerId)
            {
                Player1Board = board;
                Player1Ready = true;
            }
            else if (Player2?.Id == playerId)
            {
                Player2Board = board;
                Player2Ready = true;
            }
        }

        public bool AreBothPlayersReady()
        {
            return Player1Ready && Player2Ready;
        }

        public GameBoard GetPlayerBoard(string playerId)
        {
            if (Player1?.Id == playerId) return Player1Board;
            if (Player2?.Id == playerId) return Player2Board;
            return null;
        }
    }

    public enum GameSessionStatus
    {
        PlacingShips,
        InProgress,
        Finished
    }

    public class GameServer
    {
        private TcpListener _listener;
        private bool _isRunning;
        private ConcurrentDictionary<string, ConnectedPlayer> _players =
            new ConcurrentDictionary<string, ConnectedPlayer>();
        private ConcurrentDictionary<string, NetworkStream> _playerStreams =
            new ConcurrentDictionary<string, NetworkStream>();
        private LobbyManager _lobbyManager;
        private ConcurrentDictionary<string, GameSession> _gameSessions =
            new ConcurrentDictionary<string, GameSession>();
        private SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public GameServer()
        {
            _lobbyManager = new LobbyManager(_players);
            _lobbyManager.SetGameServer(this);
        }

        public async Task StartAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine("==========================================");
            Console.WriteLine($"Сервер запущен на порту {port}");
            Console.WriteLine($"IP адрес: {IPAddress.Any}");
            Console.WriteLine("==========================================");
            Console.WriteLine("Ожидание подключений...");
            Console.WriteLine();

            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка принятия подключения: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient)
        {
            string connectionId = Guid.NewGuid().ToString();
            string playerId = null;
            Console.WriteLine($"Новое подключение: {connectionId}");

            try
            {
                using (tcpClient)
                using (var stream = tcpClient.GetStream())
                {
                    await ClearStreamBufferAsync(stream);

                    while (tcpClient.Connected)
                    {
                        try
                        {
                            if (!stream.DataAvailable)
                            {
                                await Task.Delay(10);
                                continue;
                            }
       
                            byte[] lengthBytes = new byte[4];
                            int lengthBytesRead = 0;
       
                            while (lengthBytesRead < 4)
                            {
                                int read = await stream.ReadAsync(lengthBytes, lengthBytesRead, 4 - lengthBytesRead);
                                if (read == 0) break;
                                lengthBytesRead += read;
                            }
       
                            if (lengthBytesRead < 4) break;
       
                            int messageLength = BitConverter.ToInt32(lengthBytes, 0);
       
                            if (messageLength <= 0 || messageLength > 10 * 1024 * 1024)
                            {
                                Console.WriteLine($"Некорректный размер сообщения: {messageLength}, пропускаем");
                                continue;
                            }
       
                            byte[] messageBytes = new byte[messageLength];
                            int totalBytesRead = 0;
       
                            while (totalBytesRead < messageLength)
                            {
                                int bytesRead = await stream.ReadAsync(messageBytes, totalBytesRead,
                                    Math.Min(8192, messageLength - totalBytesRead));
                                if (bytesRead == 0) break;
                                totalBytesRead += bytesRead;
                            }
       
                            if (totalBytesRead < messageLength)
                            {
                                Console.WriteLine($"Неполное сообщение: {totalBytesRead}/{messageLength}");
                                break;
                            }
       
                            string json = Encoding.UTF8.GetString(messageBytes, 0, totalBytesRead);

                            if (json.Length > 0 && json.TrimStart()[0] == '{')
                            {
                                var message = NetworkMessage.FromJson(json);

                                if (message != null)
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Получено: {message.Type} от {connectionId}");

                                    var response = await ProcessMessageAsync(message, connectionId, stream);

                                    if (response != null)
                                    {
                                        await Task.Delay(1);
                                        await SendMessageAsync(stream, response);
                                    }

                                    if (message.Type == MessageType.Connect && response != null)
                                    {
                                        var data = response.Data?.ToObject<ConnectResponseData>();
                                        if (data != null)
                                        {
                                            playerId = data.PlayerId;
                                            _playerStreams[playerId] = stream;
                                            Console.WriteLine($"Игрок {playerId} зарегистрирован");

                                            await Task.Delay(5);
                                            await SendRoomsListToClient(stream, playerId);
                                            BroadcastRoomsList();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Получен не-JSON данные: {json.Substring(0, Math.Min(50, json.Length))}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}");
                            await Task.Delay(100);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента {connectionId}: {ex.Message}");
            }
            finally
            {
                if (_playerStreams.TryGetValue(playerId, out var stream))
                {
                    await ClearStreamBufferAsync(stream);
                }

                var playerToRemove = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (playerToRemove != null)
                {
                    playerId = playerToRemove.Id;
                    Console.WriteLine($"Игрок отключился: {playerToRemove.Name}");

                    var room = _lobbyManager.GetPlayerRoom(playerToRemove.Id);
                    if (room == null)
                    {
                        _players.TryRemove(playerToRemove.Id, out _);
                        _playerStreams.TryRemove(playerToRemove.Id, out _);
                    }
                    else
                    {
                        playerToRemove.Status = PlayerStatus.Offline;
                        _lobbyManager.LeaveRoom(playerToRemove.Id);
                    }

                    await Task.Delay(5);
                    BroadcastRoomsList();
                }

                Console.WriteLine($"Подключение закрыто: {connectionId}");
            }
        }

        public async void BroadcastRoomsList()
        {
            await _sendLock.WaitAsync();
            try
            {
                foreach (var kvp in _playerStreams.ToList())
                {
                    try
                    {
                        var playerId = kvp.Key;
                        var stream = kvp.Value;

                        var rooms = _lobbyManager.GetRoomsForPlayer(playerId);

                        var message = new NetworkMessage
                        {
                            Type = MessageType.RoomsList,
                            SenderId = "SERVER",
                            Data = JObject.FromObject(new
                            {
                                rooms = rooms,
                                count = rooms.Count
                            })
                        };

                        string json = message.ToJson();
                        byte[] data = Encoding.UTF8.GetBytes(json);
                        byte[] length = BitConverter.GetBytes(data.Length);
                        byte[] sendBuffer = new byte[4 + data.Length];
                        Buffer.BlockCopy(length, 0, sendBuffer, 0, 4);
                        Buffer.BlockCopy(data, 0, sendBuffer, 4, data.Length);

                        await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
                        await stream.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка отправки игроку {kvp.Key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка BroadcastRoomsList: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendRoomsListToClient(NetworkStream stream, string playerId)
        {
            try
            {
                var rooms = _lobbyManager.GetRoomsForPlayer(playerId);
                var message = new NetworkMessage
                {
                    Type = MessageType.RoomsList,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(new
                    {
                        rooms = rooms,
                        count = rooms.Count
                    })
                };

                await SendMessageAsync(stream, message);
                Console.WriteLine($"Список комнат отправлен конкретному клиенту");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки списка комнат клиенту: {ex.Message}");
            }
        }

        private async Task<NetworkMessage> ProcessMessageAsync(NetworkMessage message, string connectionId, NetworkStream stream)
        {
            try
            {
                if (message == null) return null;

                Console.WriteLine($"ProcessMessageAsync: {message.Type}");

                switch (message.Type)
                {
                    case MessageType.Connect:
                        return HandleConnect(message, connectionId, stream);

                    case MessageType.ChatMessage:
                        return HandleChatMessage(message);

                    case MessageType.CreateRoom:
                        return await HandleCreateRoom(message, connectionId);

                    case MessageType.JoinRoom:
                        return await HandleJoinRoom(message, connectionId);

                    case MessageType.GetRooms:
                        var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
                        if (player == null)
                            return new NetworkMessage
                            {
                                Type = MessageType.Error,
                                Data = JObject.FromObject(new { Message = "Игрок не найден" })
                            };
                        return HandleGetRooms(player.Id);

                    case MessageType.LeaveRoom:
                        return await HandleLeaveRoom(message, connectionId);

                    case MessageType.StartGame:
                        return await HandleStartGame(message, connectionId);

                    case MessageType.GameReady:
                        _ = HandleGameReadyAsync(message, connectionId);
                        return null;

                    case MessageType.Attack:
                        return await HandleAttack(message, connectionId);

                    case MessageType.GameOver:
                        return await HandleGameOver(message, connectionId);

                    case MessageType.Ping:
                        return new NetworkMessage { Type = MessageType.Pong, SenderId = "SERVER" };

                    case MessageType.Disconnect:
                        Console.WriteLine($"Клиент {connectionId} инициировал отключение");
                        return null;

                    default:
                        return new NetworkMessage
                        {
                            Type = MessageType.Error,
                            Data = JObject.FromObject(new { Message = $"Неизвестный тип: {message.Type}" })
                        };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в ProcessMessageAsync: {ex.Message}");
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = $"Ошибка: {ex.Message}" })
                };
            }
        }

        private void UpdatePlayerIdInRooms(string oldPlayerId, string newPlayerId, string connectionId)
        {
            try
            {
                Console.WriteLine($"Обновление ID игрока в комнатах: {oldPlayerId} -> {newPlayerId}");

                // Ищем все комнаты, где игрок был создателем или вторым игроком
                foreach (var room in _lobbyManager.GetAllRooms())
                {
                    bool updated = false;

                    if (room.Creator?.Id == oldPlayerId)
                    {
                        room.Creator.Id = newPlayerId;
                        room.Creator.ConnectionId = connectionId;
                        updated = true;
                        Console.WriteLine($"Обновлен Creator комнаты {room.Name}: {oldPlayerId} -> {newPlayerId}");
                    }

                    if (room.Player2?.Id == oldPlayerId)
                    {
                        room.Player2.Id = newPlayerId;
                        room.Player2.ConnectionId = connectionId;
                        updated = true;
                        Console.WriteLine($"Обновлен Player2 комнаты {room.Name}: {oldPlayerId} -> {newPlayerId}");
                    }

                    if (updated)
                    {
                        // Обновляем в словаре игроков, если ID изменился
                        if (oldPlayerId != newPlayerId)
                        {
                            if (_players.TryGetValue(oldPlayerId, out var player))
                            {
                                _players.TryRemove(oldPlayerId, out _);
                                player.Id = newPlayerId;
                                player.ConnectionId = connectionId;
                                _players[newPlayerId] = player;
                                Console.WriteLine($"Игрок {player.Name} обновлен в словаре: {oldPlayerId} -> {newPlayerId}");
                            }
                        }
                    }
                }

                // После обновления комнат, отправляем обновленный список всем
                BroadcastRoomsList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления ID игрока: {ex.Message}");
            }
        }

        private NetworkMessage HandleConnect(NetworkMessage message, string connectionId, NetworkStream stream)
        {
            var data = message.Data.ToObject<ConnectData>();

            // ВАЖНО: Ищем по ConnectionId, а не по имени!
            var existingPlayer = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);

            ConnectedPlayer player;
            string playerId;

            if (existingPlayer != null)
            {
                // Игрок уже есть - используем его ID
                playerId = existingPlayer.Id;
                Console.WriteLine($"Игрок {data.PlayerName} переподключается с ID: {playerId}");

                existingPlayer.Name = data.PlayerName;
                existingPlayer.Status = PlayerStatus.Online;
                existingPlayer.LastSeen = DateTime.UtcNow;

                player = existingPlayer;
            }
            else
            {
                // Новый игрок
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

        private NetworkMessage HandleChatMessage(NetworkMessage message)
        {
            var data = message.Data.ToObject<ChatMessageData>();
            Console.WriteLine($"Чат от {message.SenderId}: {data.Message}");

            return new NetworkMessage
            {
                Type = MessageType.ChatMessage,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    Message = $"Сообщение получено: {data.Message}",
                    OriginalSender = data.SenderName
                })
            };
        }

        private async Task<NetworkMessage> HandleCreateRoom(NetworkMessage message, string connectionId)
        {
            try
            {
                Console.WriteLine("=== СОЗДАНИЕ КОМНАТЫ ===");

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
                Console.WriteLine($"Создаем комнату '{roomName}' для игрока {player.Name} (ID: {player.Id})");

                var room = _lobbyManager.CreateRoom(roomName, player);

                Console.WriteLine($"Комната создана: ID={room.Id}, Name={room.Name}");

                // Отправляем персональное уведомление
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
                    Console.WriteLine($"Отправлено RoomCreated клиенту {player.Id}");
                }

                // Обновляем список комнат для всех
                Console.WriteLine("Вызываем BroadcastRoomsList...");
                BroadcastRoomsList();

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ОШИБКА создания комнаты: {ex.Message}");
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
                Console.WriteLine("=== ОБРАБОТКА JoinRoom ===");

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
                    Console.WriteLine($"Игрок {player.Name} присоединился к комнате {room?.Name}");

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
                            
                            // Отправляем сообщение StartGame обоим игрокам, чтобы они перешли на страницу игры
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

                            // Отправляем сообщение StartGame обоим игрокам
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

                        // Обновляем статус комнаты и отправляем обновленный список всем игрокам
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
            Console.WriteLine($"=== ВЫЗОВ HandleGetRooms для игрока {playerId} ===");

            var rooms = _lobbyManager.GetRoomsForPlayer(playerId);
            Console.WriteLine($"Получено комнат из LobbyManager: {rooms.Count}");

            foreach (var room in rooms)
            {
                Console.WriteLine($"  - Комната в ответе: {room.Name}, ID: {room.Id}, IsMyRoom: {room.IsMyRoom}");
            }

            var response = new NetworkMessage
            {
                Type = MessageType.RoomsList,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    rooms = rooms,
                    count = rooms.Count
                })
            };

            Console.WriteLine($"Сформирован ответ RoomsList с {rooms.Count} комнатами");
            return response;
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
            if (room != null)
            {
                var opponent = room.GetOpponent(player.Id);
                _lobbyManager.LeaveRoom(player.Id);

                Console.WriteLine($"Игрок {player.Name} покинул комнату");

                // Обновляем список комнат для всех
                BroadcastRoomsList();

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
            }
            else
            {
                // Если игрок не был в комнате, отправляем ему обновленный список комнат
                BroadcastRoomsList();
            }

            return new NetworkMessage
            {
                Type = MessageType.JoinedRoom,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    Message = "Вы покинули комнату"
                })
            };
        }

        private async Task<NetworkMessage> HandleStartGame(NetworkMessage message, string connectionId)
        {
            try
            {
                var data = message.Data.ToObject<JoinRoomData>();
                Console.WriteLine($"=== НАЧАЛО ИГРЫ ===");
                Console.WriteLine($"RoomId: {data.RoomId}, ConnectionId: {connectionId}");

                var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player == null)
                {
                    Console.WriteLine("Ошибка: игрок не найден");
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Игрок не найден" })
                    };
                }

                var room = _lobbyManager.GetPlayerRoom(player.Id);
                if (room == null)
                {
                    Console.WriteLine("Ошибка: комната не найдена");
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Комната не найдена" })
                    };
                }

                if (room.Creator?.Id != player.Id)
                {
                    Console.WriteLine("Ошибка: только создатель может начать игру");
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Только создатель комнаты может начать игру" })
                    };
                }

                if (!room.IsFull)
                {
                    Console.WriteLine("Ошибка: комната не полна");
                    return new NetworkMessage
                    {
                        Type = MessageType.Error,
                        Data = JObject.FromObject(new { Message = "Ожидание второго игрока" })
                    };
                }

                if (_lobbyManager.StartGame(data.RoomId))
                {
                    Console.WriteLine($"Игра началась в комнате {room.Name}");

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
                        Console.WriteLine($"Уведомление отправлено создателю {room.Creator.Name}");
                    }

                    if (room.Player2 != null && _playerStreams.TryGetValue(room.Player2.Id, out var player2Stream))
                    {
                        await SendMessageAsync(player2Stream, startGameMessage);
                        Console.WriteLine($"Уведомление отправлено игроку {room.Player2.Name}");
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
                
                // Просто отправляем состояние игры без повторной отправки StartGame
                await SendGameStateToBothPlayers(gameSession, room);
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

                // Сначала отправляем результат последнего выстрела обоим, чтобы отобразить его на досках
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

                // Затем уведомляем о конце игры и победе/поражении
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
                    var winnerMessage = new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    };
                    await SendMessageAsync(winnerStream, winnerMessage);
                }

                if (_playerStreams.TryGetValue(opponent.Id, out var loserStream))
                {
                    var loserMessage = new NetworkMessage
                    {
                        Type = MessageType.GameOver,
                        SenderId = "SERVER",
                        Data = JObject.FromObject(gameOverData)
                    };
                    await SendMessageAsync(loserStream, loserMessage);
                }

                _lobbyManager.RemoveRoom(room.Id);
                _gameSessions.TryRemove(room.Id, out _);
                BroadcastRoomsList();

                return null;
            }

            if (_playerStreams.TryGetValue(player.Id, out var attackerStream))
            {
                var resultMessage = new NetworkMessage
                {
                    Type = MessageType.AttackResult,
                    SenderId = player.Id,
                    Data = JObject.FromObject(attackResult)
                };
                await SendMessageAsync(attackerStream, resultMessage);
            }

            if (_playerStreams.TryGetValue(opponent.Id, out var defenderStream))
            {
                var defenderResultMessage = new NetworkMessage
                {
                    Type = MessageType.AttackResult,
                    SenderId = player.Id,
                    Data = JObject.FromObject(attackResult)
                };
                await SendMessageAsync(defenderStream, defenderResultMessage);
            }

            // Повторная проверка по сетке после отправки AttackResult
            if (opponentBoard.HasNoShipCellsLeft())
            {
                gameSession.Status = GameSessionStatus.Finished;
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
                        TimeLeft = 30
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

                // Игрок сдался — уведомляем противника (победителя) о победе и перекидываем в лобби
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

                _lobbyManager.RemoveRoom(room.Id);
                _gameSessions.TryRemove(room.Id, out _);

                BroadcastRoomsList();
            }

            return null;
        }

        private async Task SendGameStateToBothPlayers(GameSession gameSession, GameRoom room)
        {
            var player1Board = gameSession.GetPlayerBoard(room.Creator.Id);
            var player2Board = gameSession.GetPlayerBoard(room.Player2.Id);

            if (player1Board == null || player2Board == null)
            {
                Console.WriteLine($"Ошибка: одна из игровых досок не найдена для комнаты {room.Id}");
                return;
            }

            var stateForPlayer1 = new GameState
            {
                RoomId = room.Id,
                MyBoard = player1Board,
                EnemyBoard = player2Board,
                CurrentTurnPlayerId = gameSession.CurrentTurnPlayerId,
                MyPlayerId = room.Creator.Id,
                EnemyPlayerId = room.Player2.Id,
                EnemyPlayerName = room.Player2.Name,
                Phase = "InGame"
            };

            if (_playerStreams.TryGetValue(room.Creator.Id, out var creatorStream))
            {
                var stateMessage = new NetworkMessage
                {
                    Type = MessageType.GameState,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(stateForPlayer1)
                };
                await SendMessageAsync(creatorStream, stateMessage);
            }
            else
            {
                Console.WriteLine($"Не удалось получить поток для игрока {room.Creator.Name} ({room.Creator.Id})");
            }

            var stateForPlayer2 = new GameState
            {
                RoomId = room.Id,
                MyBoard = player2Board,
                EnemyBoard = player1Board,
                CurrentTurnPlayerId = gameSession.CurrentTurnPlayerId,
                MyPlayerId = room.Player2.Id,
                EnemyPlayerId = room.Creator.Id,
                EnemyPlayerName = room.Creator.Name,
                Phase = "InGame"
            };

            if (_playerStreams.TryGetValue(room.Player2.Id, out var player2Stream))
            {
                var stateMessage = new NetworkMessage
                {
                    Type = MessageType.GameState,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(stateForPlayer2)
                };
                await SendMessageAsync(player2Stream, stateMessage);
            }
            else
            {
                Console.WriteLine($"Не удалось получить поток для игрока {room.Player2.Name} ({room.Player2.Id})");
            }

            // После начала игры обновляем статус комнаты и отправляем обновленный список всем игрокам
            BroadcastRoomsList();
        }

        private async Task SendMessageAsync(NetworkStream stream, NetworkMessage message)
        {
            await _sendLock.WaitAsync();
            try
            {
                string json = message.ToJson();
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                Console.WriteLine($"Отправка {message.Type}: {data.Length} байт");

                byte[] sendBuffer = new byte[4 + data.Length];
                Buffer.BlockCopy(length, 0, sendBuffer, 0, 4);
                Buffer.BlockCopy(data, 0, sendBuffer, 4, data.Length);

                await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SkipRemainingDataAsync(NetworkStream stream)
        {
            try
            {
                if (stream.DataAvailable)
                {
                    byte[] buffer = new byte[4096];
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    Console.WriteLine($"Пропущено {read} байт остаточных данных");
                }
            }
            catch { }
        }

        private async Task ClearStreamBufferAsync(NetworkStream stream)
        {
            try
            {
                if (stream != null && stream.CanRead && stream.DataAvailable)
                {
                    byte[] buffer = new byte[4096];
                    int totalRead = 0;
                    while (stream.DataAvailable)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        totalRead += read;
                        await Task.Delay(1);
                    }
                    if (totalRead > 0)
                    {
                        Console.WriteLine($"Очищено {totalRead} байт из буфера");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка очистки буфера: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("Сервер остановлен");
        }
    }
}