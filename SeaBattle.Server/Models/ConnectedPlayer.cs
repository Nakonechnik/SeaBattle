using System;
using SeaBattle.Shared.Models;

namespace SeaBattle.Server.Models
{
    public class ConnectedPlayer
    {
        public string Id { get; set; }
        public string ConnectionId { get; set; }
        public string Name { get; set; }
        public PlayerStatus Status { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastSeen { get; set; }
    }
}