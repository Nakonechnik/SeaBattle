using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SeaBattle.Shared.Models
{
    public class Ship
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("cells")]
        public List<ShipCell> Cells { get; set; } = new List<ShipCell>();

        [JsonProperty("isPlaced")]
        public bool IsPlaced { get; set; }

        public bool IsDestroyed => Cells != null && Cells.All(c => c.IsHit);

        public Ship() { }

        public Ship(int size)
        {
            Size = size;
        }
    }

    public class ShipCell
    {
        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("isHit")]
        public bool IsHit { get; set; }
    }
}
