using System.Collections.Generic;
using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
    public class AttackResult
    {
        [JsonProperty("attackerId")]
        public string AttackerId { get; set; }

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

        [JsonProperty("shipCells")]
        public List<ShipCell> ShipCells { get; set; }

        [JsonProperty("changedCells")]
        public List<ShipCell> ChangedCells { get; set; }
    }
}
