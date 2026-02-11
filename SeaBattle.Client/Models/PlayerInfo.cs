namespace SeaBattle.Client
{
    public class PlayerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public PlayerStatus Status { get; set; }
        public bool IsConnected { get; set; }

        public override string ToString()
        {
            return $"{Name} ({Status})";
        }
    }

    public enum PlayerStatus
    {
        Offline = 0,
        Online = 1,
        InLobby = 2,
        InGame = 3,
        Searching = 4
    }

    public static class PlayerSession
    {
        public static PlayerInfo Current { get; set; }
    }
}