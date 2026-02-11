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
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("cells")]
        public List<ShipCell> Cells { get; set; } = new List<ShipCell>();

        [JsonProperty("isPlaced")]
        public bool IsPlaced { get; set; }

        [JsonProperty("isDestroyed")]
        public bool IsDestroyed => Cells != null && Cells.All(c => c.IsHit);

        public Ship(int size)
        {
            Size = size;
        }
    }

    // Клетка корабля
    public class ShipCell
    {
        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("isHit")]
        public bool IsHit { get; set; }
    }

    // Игровое поле
    public class GameBoard
    {
        public const int BoardSize = 10;

        [JsonProperty("cells")]
        public CellState[,] Cells { get; set; } = new CellState[BoardSize, BoardSize];

        [JsonProperty("ships")]
        public List<Ship> Ships { get; set; } = new List<Ship>();

        [JsonProperty("visibleCells")]
        public bool[,] VisibleCells { get; set; } = new bool[BoardSize, BoardSize];

        public GameBoard()
        {
            // Инициализация пустого поля
            for (int x = 0; x < BoardSize; x++)
            {
                for (int y = 0; y < BoardSize; y++)
                {
                    Cells[x, y] = CellState.Empty;
                    VisibleCells[x, y] = false;
                }
            }

            // Добавляем корабли по умолчанию
            Ships.Add(new Ship(4)); // 1 четырехпалубный
            Ships.Add(new Ship(3)); // 1 трехпалубный
            Ships.Add(new Ship(3)); // 2 трехпалубный
            Ships.Add(new Ship(2)); // 1 двухпалубный
            Ships.Add(new Ship(2)); // 2 двухпалубный
            Ships.Add(new Ship(2)); // 3 двухпалубный
            Ships.Add(new Ship(1)); // 1 однопалубный
            Ships.Add(new Ship(1)); // 2 однопалубный
            Ships.Add(new Ship(1)); // 3 однопалубный
            Ships.Add(new Ship(1)); // 4 однопалубный
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
                            if (Cells[checkX, checkY] == CellState.Ship)
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

                Cells[cellX, cellY] = CellState.Ship;
                ship.Cells.Add(new ShipCell { X = cellX, Y = cellY });
            }
        }

        // Случайная расстановка
        public void RandomPlacement()
        {
            var random = new Random();

            foreach (var ship in Ships)
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
        public bool IsReady => Ships.All(s => s.IsPlaced);

        // Проверка все ли корабли уничтожены
        public bool AllShipsDestroyed => Ships.All(s => s.IsDestroyed);

        // Атака
        public AttackResult Attack(int x, int y)
        {
            if (x < 0 || x >= BoardSize || y < 0 || y >= BoardSize)
            {
                return new AttackResult { IsValid = false, Message = "Неверные координаты" };
            }

            if (Cells[x, y] == CellState.Hit || Cells[x, y] == CellState.Miss)
            {
                return new AttackResult { IsValid = false, Message = "Сюда уже стреляли" };
            }

            VisibleCells[x, y] = true;

            if (Cells[x, y] == CellState.Ship)
            {
                Cells[x, y] = CellState.Hit;

                // Находим корабль
                var ship = Ships.FirstOrDefault(s =>
                    s.Cells.Any(c => c.X == x && c.Y == y));

                if (ship != null)
                {
                    var cell = ship.Cells.First(c => c.X == x && c.Y == y);
                    cell.IsHit = true;

                    if (ship.IsDestroyed)
                    {
                        // Отмечаем все клетки уничтоженного корабля
                        foreach (var c in ship.Cells)
                        {
                            Cells[c.X, c.Y] = CellState.Destroyed;
                        }

                        // Отмечаем ореол
                        MarkPerimeter(ship);

                        return new AttackResult
                        {
                            IsValid = true,
                            IsHit = true,
                            IsDestroyed = true,
                            ShipSize = ship.Size,
                            Message = "Корабль уничтожен!"
                        };
                    }

                    return new AttackResult
                    {
                        IsValid = true,
                        IsHit = true,
                        IsDestroyed = false,
                        Message = "Попадание!"
                    };
                }
            }
            else
            {
                Cells[x, y] = CellState.Miss;
                return new AttackResult
                {
                    IsValid = true,
                    IsHit = false,
                    IsDestroyed = false,
                    Message = "Промах"
                };
            }

            return new AttackResult { IsValid = false, Message = "Ошибка атаки" };
        }

        // Отметка ореола вокруг уничтоженного корабля
        private void MarkPerimeter(Ship ship)
        {
            foreach (var cell in ship.Cells)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = cell.X + dx;
                        int y = cell.Y + dy;

                        if (x >= 0 && x < BoardSize && y >= 0 && y < BoardSize)
                        {
                            if (Cells[x, y] == CellState.Empty)
                            {
                                Cells[x, y] = CellState.Miss;
                                VisibleCells[x, y] = true;
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
        [JsonProperty("isValid")]
        public bool IsValid { get; set; }

        [JsonProperty("isHit")]
        public bool IsHit { get; set; }

        [JsonProperty("isDestroyed")]
        public bool IsDestroyed { get; set; }

        [JsonProperty("shipSize")]
        public int ShipSize { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("isGameOver")]
        public bool IsGameOver { get; set; }

        [JsonProperty("winnerId")]
        public string WinnerId { get; set; }
    }

    // Состояние игры
    public class GameState
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("myBoard")]
        public GameBoard MyBoard { get; set; }

        [JsonProperty("enemyBoard")]
        public GameBoard EnemyBoard { get; set; }

        [JsonProperty("currentTurnPlayerId")]
        public string CurrentTurnPlayerId { get; set; }

        [JsonProperty("myPlayerId")]
        public string MyPlayerId { get; set; }

        [JsonProperty("enemyPlayerId")]
        public string EnemyPlayerId { get; set; }

        [JsonProperty("enemyPlayerName")]
        public string EnemyPlayerName { get; set; }

        [JsonProperty("phase")]
        public string Phase { get; set; } // Placement, Battle, Finished

        [JsonProperty("timeLeft")]
        public int TimeLeft { get; set; }
    }

    // Данные для размещения кораблей
    public class PlaceShipsData
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("ships")]
        public List<ShipPlacement> Ships { get; set; }
    }

    public class ShipPlacement
    {
        [JsonProperty("shipId")]
        public string ShipId { get; set; }

        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("isHorizontal")]
        public bool IsHorizontal { get; set; }
    }

    // Данные для атаки
    public class AttackData
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }
    }
}