using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;

namespace SeaBattle.Server
{
    public class LobbyManager
    {
        private readonly ConcurrentDictionary<string, GameRoom> _rooms = new ConcurrentDictionary<string, GameRoom>();
        private readonly ConcurrentDictionary<string, ConnectedPlayer> _players;
        private GameServer _gameServer;

        public LobbyManager(ConcurrentDictionary<string, ConnectedPlayer> players)
        {
            _players = players;
        }

        public void SetGameServer(GameServer gameServer)
        {
            _gameServer = gameServer;
        }

        public GameRoom CreateRoom(string roomName, ConnectedPlayer creator)
        {
            var existingRoom = GetPlayerRoom(creator.Id);
            if (existingRoom != null)
            {
                return existingRoom;
            }

            var room = new GameRoom
            {
                Name = string.IsNullOrEmpty(roomName) ? $"Комната {_rooms.Count + 1}" : roomName,
                Creator = creator
            };

            _rooms[room.Id] = room;
            creator.Status = PlayerStatus.InRoom;

            Console.WriteLine($"Создана комната: {room.Name} (ID: {room.Id}) создателем {creator.Name}");

            return room;
        }

        public bool JoinRoom(string roomId, ConnectedPlayer player)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return false;
            }

            if (room.IsFull)
            {
                return false;
            }

            if (room.Creator?.Id == player.Id)
            {
                return false;
            }

            try
            {
                room.AddPlayer(player);
                player.Status = PlayerStatus.InRoom;

                Console.WriteLine($"Игрок {player.Name} присоединился к комнате {room.Name}");

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void LeaveRoom(string playerId)
        {
            foreach (var room in _rooms.Values)
            {
                if (room.ContainsPlayer(playerId))
                {
                    bool wasCreator = (room.Creator?.Id == playerId);
                    bool wasPlayer2 = (room.Player2?.Id == playerId);

                    room.RemovePlayer(playerId);

                    if (_players.TryGetValue(playerId, out var player))
                    {
                        player.Status = PlayerStatus.Online;
                    }

                    Console.WriteLine($"Игрок {playerId} покинул комнату {room.Name}");

                    if (room.IsEmpty)
                    {
                        _rooms.TryRemove(room.Id, out _);
                        Console.WriteLine($"Комната {room.Name} удалена (пустая)");
                    }
                    else if (wasCreator && room.Player2 != null)
                    {
                        room.Creator = room.Player2;
                        room.Player2 = null;
                        room.Status = GameRoomStatus.Waiting;
                        Console.WriteLine($"Права создателя переданы игроку {room.Creator.Name}");
                    }

                    break;
                }
            }
        }

        public List<ClientRoomInfo> GetRoomsForPlayer(string playerId)
        {
            Console.WriteLine($"GetRoomsForPlayer для игрока {playerId}, всего комнат: {_rooms.Count}");

            var allRooms = _rooms.Values
                .Where(r => r.Status == GameRoomStatus.Waiting || r.Status == GameRoomStatus.Full || r.Status == GameRoomStatus.InGame)
                .Select(r => new ClientRoomInfo
                {
                    Id = r.Id,
                    Name = r.Name,
                    CreatorId = r.Creator?.Id,
                    CreatorName = r.Creator?.Name ?? "Неизвестно",
                    PlayerCount = (r.Creator != null ? 1 : 0) + (r.Player2 != null ? 1 : 0),
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt,
                    IsMyRoom = r.Creator?.Id == playerId || r.Player2?.Id == playerId
                })
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            foreach (var room in allRooms)
            {
                Console.WriteLine($"  - Комната: {room.Name}, ID: {room.Id}, IsMyRoom: {room.IsMyRoom}");
            }

            return allRooms;
        }

        public List<GameRoom> GetAllRooms()
        {
            return _rooms.Values.ToList();
        }
        public GameRoom GetPlayerRoom(string playerId)
        {
            return _rooms.Values.FirstOrDefault(r => r.ContainsPlayer(playerId));
        }

        public bool StartGame(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                return false;
            }

            if (!room.IsFull)
            {
                return false;
            }

            room.Status = GameRoomStatus.InGame;
            room.GameStartedAt = DateTime.UtcNow;

            if (_players.TryGetValue(room.Creator.Id, out var player1))
            {
                player1.Status = PlayerStatus.InGame;
            }

            if (room.Player2 != null && _players.TryGetValue(room.Player2.Id, out var player2))
            {
                player2.Status = PlayerStatus.InGame;
            }

            Console.WriteLine($"Игра началась в комнате {room.Name}");

            return true;
        }

        public void RemoveRoom(string roomId)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                if (room.Creator != null && _players.TryGetValue(room.Creator.Id, out var creator))
                {
                    creator.Status = PlayerStatus.Online;
                }
                if (room.Player2 != null && _players.TryGetValue(room.Player2.Id, out var player2))
                {
                    player2.Status = PlayerStatus.Online;
                }

                _rooms.TryRemove(roomId, out _);
                Console.WriteLine($"Комната {room.Name} удалена");
            }
        }
    }

    // Временный класс для клиента
    public class ClientRoomInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CreatorId { get; set; }
        public string CreatorName { get; set; }
        public int PlayerCount { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsMyRoom { get; set; }
    }
}