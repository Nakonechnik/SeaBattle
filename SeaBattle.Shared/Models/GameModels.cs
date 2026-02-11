using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
    // Состояние клетки
    public enum CellState
    {
        Empty = 0,      // Пусто
        Ship = 1,       // Корабль (видно только себе)
        Hit = 2,        // Попадание
        Miss = 3,       // Промах
        Destroyed = 4   // Корабль уничтожен
    }

    // Корабль
    public class Ship
    {
        private string _id;
        private int _size;
        private List<ShipCell> _cells;
        private bool _isPlaced;

        [JsonProperty("id")]
        public string Id
        {
            get { return _id; }
            set { _id = value; }
        }

        [JsonProperty("size")]
        public int Size
        {
            get { return _size; }
            set { _size = value; }
        }

        [JsonProperty("cells")]
        public List<ShipCell> Cells
        {
            get { return _cells; }
            set { _cells = value; }
        }

        [JsonProperty("isPlaced")]
        public bool IsPlaced
        {
            get { return _isPlaced; }
            set { _isPlaced = value; }
        }

        public bool IsDestroyed
        {
            get
            {
                if (_cells == null) return false;
                return _cells.All(c => c.IsHit);
            }
        }

        public Ship()
        {
            _id = Guid.NewGuid().ToString();
            _cells = new List<ShipCell>();
        }

        public Ship(int size) : this()
        {
            _size = size;
        }
    }

    // Клетка корабля
    public class ShipCell
    {
        private int _x;
        private int _y;
        private bool _isHit;

        [JsonProperty("x")]
        public int X
        {
            get { return _x; }
            set { _x = value; }
        }

        [JsonProperty("y")]
        public int Y
        {
            get { return _y; }
            set { _y = value; }
        }

        [JsonProperty("isHit")]
        public bool IsHit
        {
            get { return _isHit; }
            set { _isHit = value; }
        }
    }

    // Игровое поле
    public class GameBoard
    {
        public const int BoardSize = 10;

        private CellState[,] _cells;
        private List<Ship> _ships;
        private bool[,] _visibleCells;

        [JsonProperty("cells")]
        public CellState[,] Cells
        {
            get { return _cells; }
            set { _cells = value; }
        }

        [JsonProperty("ships")]
        public List<Ship> Ships
        {
            get { return _ships; }
            set { _ships = value; }
        }

        [JsonProperty("visibleCells")]
        public bool[,] VisibleCells
        {
            get { return _visibleCells; }
            set { _visibleCells = value; }
        }

        public GameBoard()
        {
            _cells = new CellState[BoardSize, BoardSize];
            _visibleCells = new bool[BoardSize, BoardSize];
            _ships = new List<Ship>();

            // Инициализация пустого поля
            for (int x = 0; x < BoardSize; x++)
            {
                for (int y = 0; y < BoardSize; y++)
                {
                    _cells[x, y] = CellState.Empty;
                    _visibleCells[x, y] = false;
                }
            }

            // Добавляем корабли по умолчанию
            _ships.Add(new Ship(4)); // 1 четырехпалубный
            _ships.Add(new Ship(3)); // 1 трехпалубный
            _ships.Add(new Ship(3)); // 2 трехпалубный
            _ships.Add(new Ship(2)); // 1 двухпалубный
            _ships.Add(new Ship(2)); // 2 двухпалубный
            _ships.Add(new Ship(2)); // 3 двухпалубный
            _ships.Add(new Ship(1)); // 1 однопалубный
            _ships.Add(new Ship(1)); // 2 однопалубный
            _ships.Add(new Ship(1)); // 3 однопалубный
            _ships.Add(new Ship(1)); // 4 однопалубный
        }

        // Проверка можно ли разместить корабль
        public bool CanPlaceShip(int x, int y, int size, bool isHorizontal)
        {
            // Проверка границ
            if (isHorizontal)
            {
                if (x + size > BoardSize) return false;
            }
            else
            {
                if (y + size > BoardSize) return false;
            }

            // Проверка соседних клеток
            for (int i = 0; i < size; i++)
            {
                int cellX = isHorizontal ? x + i : x;
                int cellY = isHorizontal ? y : y + i;

                // Проверяем саму клетку и соседние
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int checkX = cellX + dx;
                        int checkY = cellY + dy;

                        if (checkX >= 0 && checkX < BoardSize &&
                            checkY >= 0 && checkY < BoardSize)
                        {
                            if (_cells[checkX, checkY] == CellState.Ship)
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        // Разместить корабль
        public void PlaceShip(Ship ship, int x, int y, bool isHorizontal)
        {
            ship.Cells.Clear();
            ship.IsPlaced = true;

            for (int i = 0; i < ship.Size; i++)
            {
                int cellX = isHorizontal ? x + i : x;
                int cellY = isHorizontal ? y : y + i;

                _cells[cellX, cellY] = CellState.Ship;
                ship.Cells.Add(new ShipCell { X = cellX, Y = cellY, IsHit = false });
            }
        }

        // Случайная расстановка
        public void RandomPlacement()
        {
            Random random = new Random();

            foreach (Ship ship in _ships)
            {
                bool placed = false;
                int attempts = 0;

                while (!placed && attempts < 1000)
                {
                    attempts++;
                    bool isHorizontal = random.Next(2) == 0;
                    int x = random.Next(isHorizontal ? BoardSize - ship.Size + 1 : BoardSize);
                    int y = random.Next(isHorizontal ? BoardSize : BoardSize - ship.Size + 1);

                    if (CanPlaceShip(x, y, ship.Size, isHorizontal))
                    {
                        PlaceShip(ship, x, y, isHorizontal);
                        placed = true;
                    }
                }
            }
        }

        // Проверка готовности (все корабли размещены)
        public bool IsReady
        {
            get
            {
                foreach (Ship ship in _ships)
                {
                    if (!ship.IsPlaced) return false;
                }
                return true;
            }
        }

        // Проверка все ли корабли уничтожены
        public bool AllShipsDestroyed
        {
            get
            {
                foreach (Ship ship in _ships)
                {
                    if (!ship.IsDestroyed) return false;
                }
                return true;
            }
        }

        // Атака
        public AttackResult Attack(int x, int y)
        {
            AttackResult result = new AttackResult();
            result.X = x;
            result.Y = y;

            if (x < 0 || x >= BoardSize || y < 0 || y >= BoardSize)
            {
                result.IsValid = false;
                result.Message = "Неверные координаты";
                return result;
            }

            if (_cells[x, y] == CellState.Hit || _cells[x, y] == CellState.Miss)
            {
                result.IsValid = false;
                result.Message = "Сюда уже стреляли";
                return result;
            }

            _visibleCells[x, y] = true;

            if (_cells[x, y] == CellState.Ship)
            {
                _cells[x, y] = CellState.Hit;

                // Находим корабль
                Ship ship = null;
                foreach (Ship s in _ships)
                {
                    foreach (ShipCell c in s.Cells)
                    {
                        if (c.X == x && c.Y == y)
                        {
                            ship = s;
                            break;
                        }
                    }
                    if (ship != null) break;
                }

                if (ship != null)
                {
                    // Находим клетку корабля
                    foreach (ShipCell cell in ship.Cells)
                    {
                        if (cell.X == x && cell.Y == y)
                        {
                            cell.IsHit = true;
                            break;
                        }
                    }

                    if (ship.IsDestroyed)
                    {
                        // Отмечаем все клетки уничтоженного корабля
                        foreach (ShipCell c in ship.Cells)
                        {
                            _cells[c.X, c.Y] = CellState.Destroyed;
                        }

                        // Отмечаем ореол
                        MarkPerimeter(ship);

                        result.IsValid = true;
                        result.IsHit = true;
                        result.IsDestroyed = true;
                        result.ShipSize = ship.Size;
                        result.Message = "Корабль уничтожен!";
                        return result;
                    }

                    result.IsValid = true;
                    result.IsHit = true;
                    result.IsDestroyed = false;
                    result.Message = "Попадание!";
                    return result;
                }
            }
            else
            {
                _cells[x, y] = CellState.Miss;
                result.IsValid = true;
                result.IsHit = false;
                result.IsDestroyed = false;
                result.Message = "Промах";
                return result;
            }

            result.IsValid = false;
            result.Message = "Ошибка атаки";
            return result;
        }

        // Отметка ореола вокруг уничтоженного корабля
        private void MarkPerimeter(Ship ship)
        {
            foreach (ShipCell cell in ship.Cells)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = cell.X + dx;
                        int y = cell.Y + dy;

                        if (x >= 0 && x < BoardSize && y >= 0 && y < BoardSize)
                        {
                            if (_cells[x, y] == CellState.Empty)
                            {
                                _cells[x, y] = CellState.Miss;
                                _visibleCells[x, y] = true;
                            }
                        }
                    }
                }
            }
        }
    }

    // Результат атаки
    public class AttackResult
    {
        private bool _isValid;
        private bool _isHit;
        private bool _isDestroyed;
        private int _shipSize;
        private string _message;
        private int _x;
        private int _y;
        private bool _isGameOver;
        private string _winnerId;

        [JsonProperty("isValid")]
        public bool IsValid
        {
            get { return _isValid; }
            set { _isValid = value; }
        }

        [JsonProperty("isHit")]
        public bool IsHit
        {
            get { return _isHit; }
            set { _isHit = value; }
        }

        [JsonProperty("isDestroyed")]
        public bool IsDestroyed
        {
            get { return _isDestroyed; }
            set { _isDestroyed = value; }
        }

        [JsonProperty("shipSize")]
        public int ShipSize
        {
            get { return _shipSize; }
            set { _shipSize = value; }
        }

        [JsonProperty("message")]
        public string Message
        {
            get { return _message; }
            set { _message = value; }
        }

        [JsonProperty("x")]
        public int X
        {
            get { return _x; }
            set { _x = value; }
        }

        [JsonProperty("y")]
        public int Y
        {
            get { return _y; }
            set { _y = value; }
        }

        [JsonProperty("isGameOver")]
        public bool IsGameOver
        {
            get { return _isGameOver; }
            set { _isGameOver = value; }
        }

        [JsonProperty("winnerId")]
        public string WinnerId
        {
            get { return _winnerId; }
            set { _winnerId = value; }
        }
    }

    // Состояние игры
    public class GameState
    {
        private string _roomId;
        private GameBoard _myBoard;
        private GameBoard _enemyBoard;
        private string _currentTurnPlayerId;
        private string _myPlayerId;
        private string _enemyPlayerId;
        private string _enemyPlayerName;
        private string _phase;
        private int _timeLeft;

        [JsonProperty("roomId")]
        public string RoomId
        {
            get { return _roomId; }
            set { _roomId = value; }
        }

        [JsonProperty("myBoard")]
        public GameBoard MyBoard
        {
            get { return _myBoard; }
            set { _myBoard = value; }
        }

        [JsonProperty("enemyBoard")]
        public GameBoard EnemyBoard
        {
            get { return _enemyBoard; }
            set { _enemyBoard = value; }
        }

        [JsonProperty("currentTurnPlayerId")]
        public string CurrentTurnPlayerId
        {
            get { return _currentTurnPlayerId; }
            set { _currentTurnPlayerId = value; }
        }

        [JsonProperty("myPlayerId")]
        public string MyPlayerId
        {
            get { return _myPlayerId; }
            set { _myPlayerId = value; }
        }

        [JsonProperty("enemyPlayerId")]
        public string EnemyPlayerId
        {
            get { return _enemyPlayerId; }
            set { _enemyPlayerId = value; }
        }

        [JsonProperty("enemyPlayerName")]
        public string EnemyPlayerName
        {
            get { return _enemyPlayerName; }
            set { _enemyPlayerName = value; }
        }

        [JsonProperty("phase")]
        public string Phase
        {
            get { return _phase; }
            set { _phase = value; }
        }

        [JsonProperty("timeLeft")]
        public int TimeLeft
        {
            get { return _timeLeft; }
            set { _timeLeft = value; }
        }
    }

    // Данные для размещения кораблей
    public class PlaceShipsData
    {
        private string _roomId;
        private List<ShipPlacement> _ships;

        [JsonProperty("roomId")]
        public string RoomId
        {
            get { return _roomId; }
            set { _roomId = value; }
        }

        [JsonProperty("ships")]
        public List<ShipPlacement> Ships
        {
            get { return _ships; }
            set { _ships = value; }
        }
    }

    public class ShipPlacement
    {
        private string _shipId;
        private int _x;
        private int _y;
        private bool _isHorizontal;

        [JsonProperty("shipId")]
        public string ShipId
        {
            get { return _shipId; }
            set { _shipId = value; }
        }

        [JsonProperty("x")]
        public int X
        {
            get { return _x; }
            set { _x = value; }
        }

        [JsonProperty("y")]
        public int Y
        {
            get { return _y; }
            set { _y = value; }
        }

        [JsonProperty("isHorizontal")]
        public bool IsHorizontal
        {
            get { return _isHorizontal; }
            set { _isHorizontal = value; }
        }
    }

    // Данные для атаки
    public class AttackData
    {
        private string _roomId;
        private int _x;
        private int _y;

        [JsonProperty("roomId")]
        public string RoomId
        {
            get { return _roomId; }
            set { _roomId = value; }
        }

        [JsonProperty("x")]
        public int X
        {
            get { return _x; }
            set { _x = value; }
        }

        [JsonProperty("y")]
        public int Y
        {
            get { return _y; }
            set { _y = value; }
        }
    }
}