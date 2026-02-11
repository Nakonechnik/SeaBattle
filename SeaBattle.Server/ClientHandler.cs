using System;
using System.IO;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SeaBattle.Server
{
    public class ClientHandler
    {
        private readonly TcpClient _client;
        private readonly string _clientId;
        private readonly GameServer _server;
        private NetworkStream _stream;
        private Player _player;

        public ClientHandler(TcpClient client, string clientId, GameServer server)
        {
            _client = client;
            _clientId = clientId;
            _server = server;
        }

        public async Task StartAsync()
        {
            try
            {
                _stream = _client.GetStream();
                var reader = new BinaryReader(_stream, Encoding.UTF8, true);

                while (_client.Connected)
                {
                    try
                    {
                        // Читаем длину сообщения (4 байта)
                        byte[] lengthBytes = reader.ReadBytes(4);
                        if (lengthBytes.Length < 4) break;

                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        // Читаем само сообщение
                        byte[] messageBytes = reader.ReadBytes(messageLength);
                        string json = Encoding.UTF8.GetString(messageBytes);

                        // Обрабатываем сообщение
                        await ProcessMessageAsync(json);
                    }
                    catch (EndOfStreamException)
                    {
                        break; // Клиент отключился
                    }
                    catch (IOException)
                    {
                        break; // Соединение разорвано
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента {_clientId}: {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        private async Task ProcessMessageAsync(string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<NetworkMessage>(json);

                Console.WriteLine($"Получено от {_clientId}: {message.Type}");

                switch (message.Type)
                {
                    case MessageType.Connect:
                        await HandleConnect(message);
                        break;

                    case MessageType.CreateRoom:
                        await HandleCreateRoom();
                        break;

                    case MessageType.JoinRoom:
                        await HandleJoinRoom(message);
                        break;

                    case MessageType.GetPlayers:
                        await HandleGetPlayers();
                        break;

                    case MessageType.Disconnect:
                        Disconnect();
                        break;

                    case MessageType.ChatMessage:
                        await HandleChatMessage(message);
                        break;

                    case MessageType.PlaceShips:
                        await HandlePlaceShips(message);
                        break;

                    case MessageType.Attack:
                        await HandleAttack(message);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}");
                await SendErrorAsync("Ошибка обработки сообщения");
            }
        }

        private async Task HandleConnect(NetworkMessage message)
        {
            try
            {
                var data = message.Data.ToObject<ConnectData>();

                // Регистрируем игрока
                _player = _server.RegisterPlayer(_clientId, data.PlayerName);

                // Отправляем успешный ответ
                var response = new NetworkMessage
                {
                    Type = MessageType.ConnectResponse,
                    Data = JObject.FromObject(new
                    {
                        PlayerId = _player.Id,
                        PlayerName = _player.Name,
                        Message = "Успешное подключение"
                    })
                };

                await SendMessageAsync(response);

                // Отправляем список игроков
                await SendOnlinePlayers();
            }
            catch (Exception ex)
            {
                await SendErrorAsync($"Ошибка подключения: {ex.Message}");
            }
        }

        private async Task HandleCreateRoom()
        {
            if (_player == null)
            {
                await SendErrorAsync("Сначала подключитесь к серверу");
                return;
            }

            var room = _server.CreateGameRoom(_player);

            var response = new NetworkMessage
            {
                Type = MessageType.RoomCreated,
                Data = JObject.FromObject(new
                {
                    RoomId = room.Id,
                    Message = "Комната создана. Ожидаем второго игрока..."
                })
            };

            await SendMessageAsync(response);
        }

        private async Task HandleJoinRoom(NetworkMessage message)
        {
            if (_player == null)
            {
                await SendErrorAsync("Сначала подключитесь к серверу");
                return;
            }

            var data = message.Data.ToObject<JoinRoomData>();

            if (_server.JoinGameRoom(data.RoomId, _player))
            {
                var response = new NetworkMessage
                {
                    Type = MessageType.JoinedRoom,
                    Data = JObject.FromObject(new
                    {
                        RoomId = data.RoomId,
                        Message = "Вы присоединились к комнате"
                    })
                };

                await SendMessageAsync(response);
            }
            else
            {
                await SendErrorAsync("Не удалось присоединиться к комнате");
            }
        }

        private async Task HandleGetPlayers()
        {
            await SendOnlinePlayers();
        }

        private async Task HandleChatMessage(NetworkMessage message)
        {
            var data = message.Data.ToObject<ChatMessageData>();
            Console.WriteLine($"Чат от {_player?.Name}: {data.Message}");

            // Здесь можно добавить логику рассылки сообщений в комнате
        }

        private async Task HandlePlaceShips(NetworkMessage message)
        {
            // Логика размещения кораблей будет добавлена позже
            Console.WriteLine($"Игрок {_player?.Name} разместил корабли");
        }

        private async Task HandleAttack(NetworkMessage message)
        {
            // Логика атаки будет добавлена позже
            Console.WriteLine($"Игрок {_player?.Name} совершил атаку");
        }

        private async Task SendOnlinePlayers()
        {
            var players = _server.GetOnlinePlayers();
            var response = new NetworkMessage
            {
                Type = MessageType.PlayersList,
                Data = JObject.FromObject(new
                {
                    Players = players
                })
            };

            await SendMessageAsync(response);
        }

        private async Task SendMessageAsync(NetworkMessage message)
        {
            try
            {
                string json = JsonConvert.SerializeObject(message);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);

                await _stream.WriteAsync(length, 0, 4);
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки сообщения: {ex.Message}");
            }
        }

        private async Task SendErrorAsync(string errorMessage)
        {
            var error = new NetworkMessage
            {
                Type = MessageType.Error,
                Data = JObject.FromObject(new
                {
                    Message = errorMessage
                })
            };

            await SendMessageAsync(error);
        }

        private void Disconnect()
        {
            try
            {
                if (_player != null)
                {
                    _server.RemovePlayer(_clientId);
                    Console.WriteLine($"Игрок отключился: {_player.Name}");
                }

                _stream?.Close();
                _client?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отключении: {ex.Message}");
            }
        }
    }
}