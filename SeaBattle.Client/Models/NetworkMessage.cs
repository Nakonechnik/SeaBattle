using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SeaBattle.Client
{
    public enum MessageType
    {
        // Системные
        Connect = 1,
        ConnectResponse = 2,
        Disconnect = 3,
        Ping = 4,
        Pong = 5,
        Error = 6,

        // Лобби
        CreateRoom = 100,
        RoomCreated = 101,
        JoinRoom = 102,
        JoinedRoom = 103,
        GetPlayers = 104,
        PlayersList = 105,

        // Игра
        PlaceShips = 200,
        ShipsPlaced = 201,
        Attack = 202,
        AttackResult = 203,
        GameState = 204,
        GameOver = 205,

        // Чат
        ChatMessage = 300
    }

    public class NetworkMessage
    {
        [JsonProperty("messageId")]
        public string MessageId { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("type")]
        public MessageType Type { get; set; }

        [JsonProperty("senderId")]
        public string SenderId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonProperty("data")]
        public object Data { get; set; }
    }
}