using SeaBattle.Server.Models;
using SeaBattle.Shared.Models;

namespace SeaBattle.Server
{
    public class GameSession
    {
        public string RoomId { get; set; }
        public ConnectedPlayer Player1 { get; set; }
        public ConnectedPlayer Player2 { get; set; }
        public GameBoard Player1Board { get; set; }
        public GameBoard Player2Board { get; set; }
        public bool Player1Ready { get; set; }
        public bool Player2Ready { get; set; }
        public GameSessionStatus Status { get; set; }
        public string CurrentTurnPlayerId { get; set; }

        public void SetPlayerReady(string playerId, GameBoard board)
        {
            if (Player1?.Id == playerId)
            {
                Player1Board = board;
                Player1Ready = true;
            }
            else if (Player2?.Id == playerId)
            {
                Player2Board = board;
                Player2Ready = true;
            }
        }

        public bool AreBothPlayersReady() => Player1Ready && Player2Ready;

        public GameBoard GetPlayerBoard(string playerId)
        {
            if (Player1?.Id == playerId) return Player1Board;
            if (Player2?.Id == playerId) return Player2Board;
            return null;
        }
    }

    public enum GameSessionStatus
    {
        PlacingShips,
        InProgress,
        Finished
    }
}
