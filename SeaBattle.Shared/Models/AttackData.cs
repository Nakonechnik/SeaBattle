using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
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
