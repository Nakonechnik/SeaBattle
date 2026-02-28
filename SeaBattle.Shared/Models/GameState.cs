using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
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
        public string Phase { get; set; }

        [JsonProperty("timeLeft")]
        public int TimeLeft { get; set; }
    }
}
