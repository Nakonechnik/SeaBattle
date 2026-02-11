using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SeaBattle.Server
{
    public class GameServer
    {
        private TcpListener _listener;
        private bool _isRunning;

        // Коллекции для хранения данных
        private ConcurrentDictionary<string, ClientHandler> _clients = new ConcurrentDictionary<string, ClientHandler>();
        private ConcurrentDictionary<string, GameRoom> _gameRooms = new ConcurrentDictionary<string, GameRoom>();
        private ConcurrentDictionary<string, Player> _players = new ConcurrentDictionary<string, Player>();

        public async Task StartAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine($"Сервер запущен на {IPAddress.Any}:{port}");

            // Основной цикл принятия подключений
            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    var clientId = Guid.NewGuid().ToString();

                    Console.WriteLine($"Новое подключение: {clientId}");

                    // Создаем обработчик клиента
                    var handler = new ClientHandler(client, clientId, this);
                    _clients[clientId] = handler;

                    // Запускаем обработку в отдельной задаче
                    Task.Run(() => handler.StartAsync());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при принятии подключения: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
        }

        // Методы для управления игроками и играми
        public Player RegisterPlayer(string clientId, string playerName)
        {
            var player = new Player
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = clientId,
                Name = playerName,
                Status = PlayerStatus.Online
            };

            _players[player.Id] = player;
            Console.WriteLine($"Зарегистрирован игрок: {playerName} ({player.Id})");

            return player;
        }

        public void RemovePlayer(string clientId)
        {
            // Находим и удаляем игрока по clientId
            foreach (var player in _players.Values)
            {
                if (player.ClientId == clientId)
                {
                    Player removedPlayer;
                    _players.TryRemove(player.Id, out removedPlayer);
                    Console.WriteLine($"Игрок удален: {player.Name}");
                    break;
                }
            }
        }

        public List<Player> GetOnlinePlayers()
        {
            var onlinePlayers = new List<Player>();
            foreach (var player in _players.Values)
            {
                if (player.Status == PlayerStatus.Online)
                {
                    onlinePlayers.Add(player);
                }
            }
            return onlinePlayers;
        }

        public GameRoom CreateGameRoom(Player player1)
        {
            var room = new GameRoom
            {
                Id = Guid.NewGuid().ToString(),
                Player1 = player1,
                Status = GameStatus.WaitingForPlayer
            };

            _gameRooms[room.Id] = room;
            player1.Status = PlayerStatus.InLobby;

            Console.WriteLine($"Создана игровая комната: {room.Id}");
            return room;
        }

        public bool JoinGameRoom(string roomId, Player player2)
        {
            GameRoom room;
            if (_gameRooms.TryGetValue(roomId, out room) && room.Player2 == null)
            {
                room.Player2 = player2;
                room.Status = GameStatus.Preparing;
                player2.Status = PlayerStatus.InLobby;

                Console.WriteLine($"Игрок {player2.Name} присоединился к комнате {roomId}");
                return true;
            }
            return false;
        }
    }
}