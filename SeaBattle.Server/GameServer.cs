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
    public partial class GameServer
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
        private ConcurrentDictionary<string, CancellationTokenSource> _turnTimers =
            new ConcurrentDictionary<string, CancellationTokenSource>();
        private ConcurrentDictionary<string, DateTime> _turnStartedAt =
            new ConcurrentDictionary<string, DateTime>();
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
                        bool gameInProgress = _gameSessions.TryGetValue(room.Id, out var session) && session.Status == GameSessionStatus.InProgress;
                        if (gameInProgress)
                        {
                            // Игра идёт — не выкидываем из комнаты, игрок может переподключиться до конца своего следующего хода
                            _playerStreams.TryRemove(playerToRemove.Id, out _);
                            playerToRemove.ConnectionId = null;
                            playerToRemove.Status = PlayerStatus.Offline;
                            var opponent = room.GetOpponent(playerToRemove.Id);
                            if (opponent != null && _playerStreams.TryGetValue(opponent.Id, out var oppStream))
                            {
                                await SendMessageAsync(oppStream, new NetworkMessage
                                {
                                    Type = MessageType.OpponentDisconnected,
                                    SenderId = "SERVER",
                                    Data = JObject.FromObject(new { PlayerName = playerToRemove.Name })
                                });
                            }
                        }
                        else
                        {
                            playerToRemove.Status = PlayerStatus.Offline;
                            _lobbyManager.LeaveRoom(playerToRemove.Id);
                        }
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
                        return await HandleChatMessage(message, connectionId);

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

                    case MessageType.ReconnectToGame:
                        return await HandleReconnectToGame(message, connectionId);

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


        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("Сервер остановлен");
        }
    }
}