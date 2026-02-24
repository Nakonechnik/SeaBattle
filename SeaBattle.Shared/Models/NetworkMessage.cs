using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace SeaBattle.Shared.Models
{
    // Добавим контракт для корректной сериализации
    public class NetworkMessageContractResolver : DefaultContractResolver
    {
        protected override JsonContract CreateContract(Type objectType)
        {
            var contract = base.CreateContract(objectType);
            return contract;
        }
    }

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

        GameReady = 110,
        GameState = 111,
        Attack = 112,
        AttackResult = 113,
        GameOver = 114,
        TurnChanged = 115,
        TimerUpdate = 116,
        ReconnectToGame = 117,
        OpponentDisconnected = 118,
        OpponentReconnected = 119,

        // Игра
        PlaceShips = 200,
        ShipsPlaced = 201,

        // Чат
        ChatMessage = 300
    }

    public class NetworkMessage
    {
        private static readonly JsonSerializerSettings _jsonSettings;

        static NetworkMessage()
        {
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new NetworkMessageContractResolver(),
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
        }

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
            try
            {
                return JsonConvert.SerializeObject(this, _jsonSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сериализации: {ex.Message}");
                return "{}";
            }
        }

        public static NetworkMessage FromJson(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonConvert.DeserializeObject<NetworkMessage>(json, _jsonSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка десериализации: {ex.Message}, JSON: {json}");
                return null;
            }
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

        [JsonProperty("pendingReconnectRoomId")]
        public string PendingReconnectRoomId { get; set; }
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

    public class TurnChangeData
    {
        [JsonProperty("nextPlayerId")]
        public string NextPlayerId { get; set; }

        [JsonProperty("previousPlayerId")]
        public string PreviousPlayerId { get; set; }

        [JsonProperty("timeLeft")]
        public int TimeLeft { get; set; }
    }

    public static class GameConstants
    {
        public const int TurnTimeSeconds = 120;
    }

    public class GameOverData
    {
        [JsonProperty("winnerId")]
        public string WinnerId { get; set; }

        [JsonProperty("winnerName")]
        public string WinnerName { get; set; }

        [JsonProperty("loserId")]
        public string LoserId { get; set; }

        [JsonProperty("loserName")]
        public string LoserName { get; set; }

        [JsonProperty("isSurrender")]
        public bool IsSurrender { get; set; }

        [JsonProperty("isTimeout")]
        public bool IsTimeout { get; set; }
    }

   
}