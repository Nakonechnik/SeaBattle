using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;

namespace SeaBattle.Server
{
    public class LobbyManager
    {
        private readonly ConcurrentDictionary<string, GameRoom> _rooms = new ConcurrentDictionary<string, GameRoom>();
        private readonly ConcurrentDictionary<string, ConnectedPlayer> _players;

        public LobbyManager(ConcurrentDictionary<string, ConnectedPlayer> players)
        {
            _players = players;
        }

        public GameRoom CreateRoom(string roomName, ConnectedPlayer creator)
        {
            var room = new GameRoom
            {
                Name = string.IsNullOrEmpty(roomName) ? $"Комната {_rooms.Count + 1}" : roomName,
                Creator = creator
            };

            _rooms[room.Id] = room;

            // Обновляем статус игрока
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
                    room.RemovePlayer(playerId);

                    if (_players.TryGetValue(playerId, out var player))
                    {
                        player.Status = PlayerStatus.Online;
                    }

                    Console.WriteLine($"Игрок {playerId} покинул комнату {room.Name}");

                    if (room.IsEmpty)
                    {
                        _rooms.TryRemove(room.Id, out _);
                        Console.WriteLine($"Комната {room.Name} удалена");
                    }

                    break;
                }
            }
        }

        public List<RoomInfo> GetAvailableRooms()
        {
            return _rooms.Values
                .Where(r => r.Status == GameRoomStatus.Waiting)
                .Select(r => new RoomInfo
                {
                    Id = r.Id,
                    Name = r.Name,
                    CreatorName = r.Creator?.Name ?? "Неизвестно",
                    PlayerCount = (r.Creator != null ? 1 : 0) + (r.Player2 != null ? 1 : 0),
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt
                })
                .ToList();
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
    }
}