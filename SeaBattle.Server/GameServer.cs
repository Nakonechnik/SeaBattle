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
using SeaBattle.Server.Models; // Для ConnectedPlayer, GameRoom

namespace SeaBattle.Server
{
    public class GameServer
    {
        private TcpListener _listener;
        private bool _isRunning;
        private ConcurrentDictionary<string, Models.ConnectedPlayer> _players =
            new ConcurrentDictionary<string, Models.ConnectedPlayer>();
        private LobbyManager _lobbyManager;

        public GameServer()
        {
            _lobbyManager = new LobbyManager(_players);
        }

        public async Task StartAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine($"Сервер запущен на порту {port}");

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
            Console.WriteLine($"Новое подключение: {connectionId}");

            try
            {
                using (tcpClient)
                using (var stream = tcpClient.GetStream())
                {
                    while (tcpClient.Connected)
                    {
                        byte[] lengthBytes = new byte[4];
                        int lengthBytesRead = await stream.ReadAsync(lengthBytes, 0, 4);
                        if (lengthBytesRead < 4) break;

                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                        byte[] messageBytes = new byte[messageLength];
                        int totalBytesRead = 0;

                        while (totalBytesRead < messageLength)
                        {
                            int bytesRead = await stream.ReadAsync(messageBytes, totalBytesRead, messageLength - totalBytesRead);
                            if (bytesRead == 0) break;
                            totalBytesRead += bytesRead;
                        }

                        string json = Encoding.UTF8.GetString(messageBytes, 0, totalBytesRead);
                        var message = NetworkMessage.FromJson(json);

                        Console.WriteLine($"Получено: {message.Type} от {connectionId}");

                        var response = await ProcessMessageAsync(message, connectionId, stream);

                        if (response != null)
                        {
                            await SendMessageAsync(stream, response);
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
                foreach (var player in _players.Values)
                {
                    if (player.ConnectionId == connectionId)
                    {
                        _lobbyManager.LeaveRoom(player.Id);
                        _players.TryRemove(player.Id, out _);
                        Console.WriteLine($"Игрок удален: {player.Name}");
                        break;
                    }
                }

                Console.WriteLine($"Подключение закрыто: {connectionId}");
            }
        }

        private async Task<NetworkMessage> ProcessMessageAsync(NetworkMessage message, string connectionId, NetworkStream stream)
        {
            try
            {
                switch (message.Type)
                {
                    case MessageType.Connect:
                        return await HandleConnect(message, connectionId);

                    case MessageType.ChatMessage:
                        return await HandleChatMessage(message);

                    case MessageType.CreateRoom:
                        return await HandleCreateRoom(message, connectionId);

                    case MessageType.JoinRoom:
                        return await HandleJoinRoom(message, connectionId);

                    case MessageType.GetRooms:
                        return await HandleGetRooms();

                    case MessageType.LeaveRoom:
                        return await HandleLeaveRoom(message, connectionId);

                    case MessageType.StartGame:
                        return await HandleStartGame(message, connectionId);

                    case MessageType.Ping:
                        return new NetworkMessage
                        {
                            Type = MessageType.Pong,
                            SenderId = "SERVER"
                        };

                    case MessageType.Disconnect:
                        return null;

                    default:
                        return new NetworkMessage
                        {
                            Type = MessageType.Error,
                            Data = JObject.FromObject(new
                            {
                                Message = $"Неизвестный тип сообщения: {message.Type}"
                            })
                        };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}");

                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new
                    {
                        Message = $"Ошибка обработки: {ex.Message}"
                    })
                };
            }
        }

        private async Task<NetworkMessage> HandleConnect(NetworkMessage message, string connectionId)
        {
            var data = message.Data.ToObject<ConnectData>();

            string playerId = Guid.NewGuid().ToString();

            var player = new Models.ConnectedPlayer
            {
                Id = playerId,
                ConnectionId = connectionId,
                Name = data.PlayerName,
                Status = Models.PlayerStatus.Online,
                ConnectedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            };

            _players[playerId] = player;

            Console.WriteLine($"Зарегистрирован игрок: {data.PlayerName} (ID: {playerId})");

            return await Task.FromResult(new NetworkMessage
            {
                Type = MessageType.ConnectResponse,
                SenderId = "SERVER",
                Data = JObject.FromObject(new ConnectResponseData
                {
                    PlayerId = playerId,
                    Message = $"Добро пожаловать, {data.PlayerName}!",
                    Success = true
                })
            });
        }

        private async Task<NetworkMessage> HandleChatMessage(NetworkMessage message)
        {
            var data = message.Data.ToObject<ChatMessageData>();

            Console.WriteLine($"Чат от {message.SenderId}: {data.Message}");

            return await Task.FromResult(new NetworkMessage
            {
                Type = MessageType.ChatMessage,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    Message = $"Сообщение получено: {data.Message}",
                    OriginalSender = data.SenderName
                })
            });
        }

        private async Task<NetworkMessage> HandleCreateRoom(NetworkMessage message, string connectionId)
        {
            var data = message.Data.ToObject<CreateRoomData>();

            var player = _players.Values.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Игрок не найден" })
                };
            }

            var room = _lobbyManager.CreateRoom(data.RoomName, player);

            return await Task.FromResult(new NetworkMessage
            {
                Type = MessageType.RoomCreated,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    RoomId = room.Id,
                    RoomName = room.Name,
                    Message = $"Комната '{room.Name}' создана"
                })
            });
        }

        private async Task<NetworkMessage> HandleJoinRoom(NetworkMessage message, string connectionId)
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

            if (_lobbyManager.JoinRoom(data.RoomId, player))
            {
                var room = _lobbyManager.GetPlayerRoom(player.Id);

                return await Task.FromResult(new NetworkMessage
                {
                    Type = MessageType.JoinedRoom,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(new
                    {
                        RoomId = data.RoomId,
                        RoomName = room?.Name,
                        Message = $"Вы присоединились к комнате '{room?.Name}'"
                    })
                });
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

        private async Task<NetworkMessage> HandleGetRooms()
        {
            var rooms = _lobbyManager.GetAvailableRooms();

            return await Task.FromResult(new NetworkMessage
            {
                Type = MessageType.RoomsList,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    Rooms = rooms,
                    Count = rooms.Count
                })
            });
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

            _lobbyManager.LeaveRoom(player.Id);

            return await Task.FromResult(new NetworkMessage
            {
                Type = MessageType.JoinedRoom,
                SenderId = "SERVER",
                Data = JObject.FromObject(new
                {
                    Message = "Вы покинули комнату"
                })
            });
        }

        private async Task<NetworkMessage> HandleStartGame(NetworkMessage message, string connectionId)
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
            if (room == null || room.Creator?.Id != player.Id)
            {
                return new NetworkMessage
                {
                    Type = MessageType.Error,
                    Data = JObject.FromObject(new { Message = "Только создатель комнаты может начать игру" })
                };
            }

            if (_lobbyManager.StartGame(data.RoomId))
            {
                return await Task.FromResult(new NetworkMessage
                {
                    Type = MessageType.StartGame,
                    SenderId = "SERVER",
                    Data = JObject.FromObject(new GameStartData
                    {
                        RoomId = data.RoomId,
                        Player1 = new PlayerInfo
                        {
                            Id = room.Creator.Id,
                            Name = room.Creator.Name,
                            Status = "InGame"
                        },
                        Player2 = room.Player2 != null ? new PlayerInfo
                        {
                            Id = room.Player2.Id,
                            Name = room.Player2.Name,
                            Status = "InGame"
                        } : null,
                        YourPlayerId = player.Id
                    })
                });
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

        private async Task SendMessageAsync(NetworkStream stream, NetworkMessage message)
        {
            try
            {
                string json = message.ToJson();
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                await stream.WriteAsync(length, 0, 4);
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();

                Console.WriteLine($"Отправлено: {message.Type}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки сообщения: {ex.Message}");
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