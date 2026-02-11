using System;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SeaBattle.Shared.Models
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
        GetRooms = 104,
        RoomsList = 105,
        LeaveRoom = 106,
        PlayerJoinedRoom = 107,
        PlayerLeftRoom = 108,
        StartGame = 109,

        // Игра
        PlaceShips = 200,
        ShipsPlaced = 201,
        Attack = 202,
        AttackResult = 203,
        GameState = 204,
        GameOver = 205,
        TurnChanged = 206,

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
        public JObject Data { get; set; }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static NetworkMessage FromJson(string json)
        {
            return JsonConvert.DeserializeObject<NetworkMessage>(json);
        }
    }

    public class ConnectData
    {
        [JsonProperty("playerName")]
        public string PlayerName { get; set; }
    }

    public class ConnectResponseData
    {
        [JsonProperty("playerId")]
        public string PlayerId { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }
    }

    public class ChatMessageData
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("senderName")]
        public string SenderName { get; set; }
    }

    public class CreateRoomData
    {
        [JsonProperty("roomName")]
        public string RoomName { get; set; }
    }

    public class JoinRoomData
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }
    }

    public class RoomInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("creatorName")]
        public string CreatorName { get; set; }

        [JsonProperty("playerCount")]
        public int PlayerCount { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    public class GameStartData
    {
        [JsonProperty("roomId")]
        public string RoomId { get; set; }

        [JsonProperty("player1")]
        public PlayerInfo Player1 { get; set; }

        [JsonProperty("player2")]
        public PlayerInfo Player2 { get; set; }

        [JsonProperty("yourPlayerId")]
        public string YourPlayerId { get; set; }
    }

    public class PlayerInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }
}