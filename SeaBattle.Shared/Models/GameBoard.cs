using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
    public class GameBoard
    {
        public const int BoardSize = 10;

        [JsonProperty("cells")]
        public CellState[,] Cells { get; set; }

        [JsonProperty("ships")]
        public List<Ship> Ships { get; set; }

        [JsonProperty("visibleCells")]
        public bool[,] VisibleCells { get; set; }

        public GameBoard()
        {
            Cells = new CellState[BoardSize, BoardSize];
            VisibleCells = new bool[BoardSize, BoardSize];
            Ships = new List<Ship>();

            for (int x = 0; x < BoardSize; x++)
                for (int y = 0; y < BoardSize; y++)
                {
                    Cells[x, y] = CellState.Empty;
                    VisibleCells[x, y] = false;
                }

            Ships.Add(new Ship(4));
            Ships.Add(new Ship(3));
            Ships.Add(new Ship(3));
            Ships.Add(new Ship(2));
            Ships.Add(new Ship(2));
            Ships.Add(new Ship(2));
            Ships.Add(new Ship(1));
            Ships.Add(new Ship(1));
            Ships.Add(new Ship(1));
            Ships.Add(new Ship(1));
        }

        public bool CanPlaceShip(int x, int y, int size, bool isHorizontal)
        {
            if (size <= 0 || size > BoardSize || x < 0 || y < 0) return false;
            if (isHorizontal && (x + size > BoardSize || y >= BoardSize)) return false;
            if (!isHorizontal && (y + size > BoardSize || x >= BoardSize)) return false;

            for (int i = 0; i < size; i++)
            {
                int cellX = isHorizontal ? x + i : x;
                int cellY = isHorizontal ? y : y + i;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int checkX = cellX + dx, checkY = cellY + dy;
                        if (checkX >= 0 && checkX < BoardSize && checkY >= 0 && checkY < BoardSize &&
                            Cells[checkX, checkY] == CellState.Ship)
                            return false;
                    }
            }
            return true;
        }

        public void PlaceShip(Ship ship, int x, int y, bool isHorizontal)
        {
            if (ship == null || ship.Size <= 0 || !CanPlaceShip(x, y, ship.Size, isHorizontal)) return;

            ship.Cells?.Clear();
            ship.IsPlaced = true;

            for (int i = 0; i < ship.Size; i++)
            {
                int cellX = isHorizontal ? x + i : x;
                int cellY = isHorizontal ? y : y + i;
                if (cellX < 0 || cellX >= BoardSize || cellY < 0 || cellY >= BoardSize)
                {
                    ship.IsPlaced = false;
                    ship.Cells?.Clear();
                    return;
                }
                Cells[cellX, cellY] = CellState.Ship;
                ship.Cells.Add(new ShipCell { X = cellX, Y = cellY, IsHit = false });
            }
        }

        public void ClearPlacement()
        {
            for (int x = 0; x < BoardSize; x++)
                for (int y = 0; y < BoardSize; y++)
                {
                    Cells[x, y] = CellState.Empty;
                    VisibleCells[x, y] = false;
                }
            if (Ships != null)
                foreach (var ship in Ships)
                {
                    ship.IsPlaced = false;
                    ship.Cells?.Clear();
                }
        }

        public void RandomPlacement()
        {
            var random = new Random();
            const int maxFullRetries = 50;

            for (int fullRetry = 0; fullRetry < maxFullRetries; fullRetry++)
            {
                ClearPlacement();
                bool allPlaced = true;

                foreach (var ship in Ships)
                {
                    var validPositions = new List<(int x, int y, bool isHorizontal)>();
                    for (int orientation = 0; orientation <= 1; orientation++)
                    {
                        bool isHorizontal = (orientation == 1);
                        int maxX = isHorizontal ? BoardSize - ship.Size + 1 : BoardSize;
                        int maxY = isHorizontal ? BoardSize : BoardSize - ship.Size + 1;
                        if (maxX <= 0 || maxY <= 0) continue;
                        for (int x = 0; x < maxX; x++)
                            for (int y = 0; y < maxY; y++)
                                if (CanPlaceShip(x, y, ship.Size, isHorizontal))
                                    validPositions.Add((x, y, isHorizontal));
                    }

                    if (validPositions.Count == 0) { allPlaced = false; break; }
                    var pos = validPositions[random.Next(validPositions.Count)];
                    PlaceShip(ship, pos.x, pos.y, pos.isHorizontal);
                }

                if (allPlaced) return;
            }
            ClearPlacement();
        }

        public bool IsReady => Ships != null && Ships.TrueForAll(s => s.IsPlaced);

        public bool HasNoShipCellsLeft()
        {
            if (Cells == null) return false;
            for (int x = 0; x < BoardSize; x++)
                for (int y = 0; y < BoardSize; y++)
                    if (Cells[x, y] == CellState.Ship) return false;
            return true;
        }

        public bool AllShipsDestroyed
        {
            get
            {
                if (HasNoShipCellsLeft()) return true;
                if (Ships == null || Ships.Count == 0) return false;
                int placed = 0, destroyed = 0;
                foreach (var ship in Ships)
                {
                    if (!ship.IsPlaced) continue;
                    placed++;
                    if (ship.IsDestroyed) destroyed++;
                }
                return placed > 0 && placed == destroyed;
            }
        }

        public AttackResult Attack(int x, int y)
        {
            var result = new AttackResult { X = x, Y = y, ChangedCells = new List<ShipCell>() };

            if (x < 0 || x >= BoardSize || y < 0 || y >= BoardSize)
            {
                result.IsValid = false;
                result.Message = "Неверные координаты";
                return result;
            }

            if (Cells[x, y] == CellState.Hit || Cells[x, y] == CellState.Miss)
            {
                result.IsValid = false;
                result.Message = "Сюда уже стреляли";
                return result;
            }

            VisibleCells[x, y] = true;
            result.ChangedCells.Add(new ShipCell { X = x, Y = y });

            if (Cells[x, y] == CellState.Ship)
            {
                Cells[x, y] = CellState.Hit;
                Ship hitShip = null;
                foreach (var s in Ships)
                    foreach (var c in s.Cells)
                        if (c.X == x && c.Y == y) { hitShip = s; break; }

                if (hitShip != null)
                {
                    foreach (var cell in hitShip.Cells)
                        if (cell.X == x && cell.Y == y) { cell.IsHit = true; break; }

                    if (hitShip.IsDestroyed)
                    {
                        foreach (var c in hitShip.Cells)
                        {
                            Cells[c.X, c.Y] = CellState.Destroyed;
                            result.ChangedCells.Add(new ShipCell { X = c.X, Y = c.Y });
                        }
                        foreach (var cell in MarkPerimeter(hitShip))
                            result.ChangedCells.Add(cell);
                        result.IsValid = true;
                        result.IsHit = true;
                        result.IsDestroyed = true;
                        result.ShipSize = hitShip.Size;
                        result.ShipCells = hitShip.Cells;
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

            Cells[x, y] = CellState.Miss;
            result.IsValid = true;
            result.IsHit = false;
            result.IsDestroyed = false;
            result.Message = "Промах";
            return result;
        }

        private List<ShipCell> MarkPerimeter(Ship ship)
        {
            var changed = new List<ShipCell>();
            foreach (var cell in ship.Cells)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = cell.X + dx, y = cell.Y + dy;
                        if (x >= 0 && x < BoardSize && y >= 0 && y < BoardSize &&
                            Cells[x, y] == CellState.Empty)
                        {
                            Cells[x, y] = CellState.Miss;
                            VisibleCells[x, y] = true;
                            changed.Add(new ShipCell { X = x, Y = y });
                        }
                    }
            return changed;
        }
    }
}
