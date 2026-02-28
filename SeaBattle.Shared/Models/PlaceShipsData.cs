using System.Collections.Generic;
using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
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
}
