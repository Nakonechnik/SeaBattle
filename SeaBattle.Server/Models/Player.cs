using System;

namespace SeaBattle.Server
{
    public class Player
    {
        public string Id { get; set; }
        public string ClientId { get; set; }
        public string Name { get; set; }
        public PlayerStatus Status { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastSeen { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        public Player()
        {
            ConnectedAt = DateTime.UtcNow;
            LastSeen = DateTime.UtcNow;
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
}