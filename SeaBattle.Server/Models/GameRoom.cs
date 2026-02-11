namespace SeaBattle.Server
{
    public class GameRoom
    {
        public string Id { get; set; }
        public Player Player1 { get; set; }
        public Player Player2 { get; set; }
        public GameStatus Status { get; set; }
        public string CurrentTurnPlayerId { get; set; }

        // Игровые поля
        public int[,] Player1Board { get; set; } = new int[10, 10];
        public int[,] Player2Board { get; set; } = new int[10, 10];

        // Открытые клетки (для каждого игрока)
        public bool[,] Player1Visible { get; set; } = new bool[10, 10];
        public bool[,] Player2Visible { get; set; } = new bool[10, 10];
    }

    public enum GameStatus
    {
        WaitingForPlayer = 0,
        Preparing = 1,        // Расстановка кораблей
        InProgress = 2,       // Идет игра
        Finished = 3          // Игра завершена
    }
}