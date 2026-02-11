using System;
using SeaBattle.Shared.Models;

namespace SeaBattle.Server.Models
{
    public class GameRoom
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public ConnectedPlayer Creator { get; set; }
        public ConnectedPlayer Player2 { get; set; }
        public GameRoomStatus Status { get; set; } = GameRoomStatus.Waiting;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? GameStartedAt { get; set; }

        public bool IsFull => Creator != null && Player2 != null;
        public bool IsEmpty => Creator == null && Player2 == null;

        public void AddPlayer(ConnectedPlayer player)
        {
            if (Creator == null)
            {
                Creator = player;
            }
            else if (Player2 == null)
            {
                Player2 = player;
                Status = GameRoomStatus.Full;
            }
            else
            {
                throw new InvalidOperationException("Комната уже полна");
            }
        }

        public void RemovePlayer(string playerId)
        {
            if (Creator?.Id == playerId)
            {
                Creator = null;
                Status = GameRoomStatus.Waiting;
            }
            else if (Player2?.Id == playerId)
            {
                Player2 = null;
                Status = GameRoomStatus.Waiting;
            }

            if (IsEmpty)
            {
                Status = GameRoomStatus.Closed;
            }
        }

        public ConnectedPlayer GetOpponent(string playerId)
        {
            if (Creator?.Id == playerId) return Player2;
            if (Player2?.Id == playerId) return Creator;
            return null;
        }

        public bool ContainsPlayer(string playerId)
        {
            return (Creator?.Id == playerId) || (Player2?.Id == playerId);
        }
    }

    public enum GameRoomStatus
    {
        Waiting = 0,
        Full = 1,
        InGame = 2,
        Finished = 3,
        Closed = 4
    }
}