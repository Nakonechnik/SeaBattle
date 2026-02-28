using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;

namespace SeaBattle.Server
{
    public partial class GameServer
    {
        private async Task SendMessageAsync(NetworkStream stream, NetworkMessage message)
        {
            await _sendLock.WaitAsync();
            try
            {
                string json = message.ToJson();
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] length = BitConverter.GetBytes(data.Length);
                byte[] sendBuffer = new byte[4 + data.Length];
                Buffer.BlockCopy(length, 0, sendBuffer, 0, 4);
                Buffer.BlockCopy(data, 0, sendBuffer, 4, data.Length);
                await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendGameStateToBothPlayers(GameSession gameSession, GameRoom room)
        {
            var player1Board = gameSession.GetPlayerBoard(room.Creator.Id);
            var player2Board = gameSession.GetPlayerBoard(room.Player2.Id);
            if (player1Board == null || player2Board == null)
            {
                Console.WriteLine($"Ошибка: одна из игровых досок не найдена для комнаты {room.Id}");
                return;
            }

            int timeLeft = GetTimeLeftForRoom(room.Id);

            var state1 = new GameState
            {
                RoomId = room.Id,
                MyBoard = player1Board,
                EnemyBoard = player2Board,
                CurrentTurnPlayerId = gameSession.CurrentTurnPlayerId,
                MyPlayerId = room.Creator.Id,
                EnemyPlayerId = room.Player2.Id,
                EnemyPlayerName = room.Player2.Name,
                Phase = "InGame",
                TimeLeft = timeLeft
            };
            if (_playerStreams.TryGetValue(room.Creator.Id, out var s1))
                await SendMessageAsync(s1, new NetworkMessage { Type = MessageType.GameState, SenderId = "SERVER", Data = JObject.FromObject(state1) });

            var state2 = new GameState
            {
                RoomId = room.Id,
                MyBoard = player2Board,
                EnemyBoard = player1Board,
                CurrentTurnPlayerId = gameSession.CurrentTurnPlayerId,
                MyPlayerId = room.Player2.Id,
                EnemyPlayerId = room.Creator.Id,
                EnemyPlayerName = room.Creator.Name,
                Phase = "InGame",
                TimeLeft = timeLeft
            };
            if (_playerStreams.TryGetValue(room.Player2.Id, out var s2))
                await SendMessageAsync(s2, new NetworkMessage { Type = MessageType.GameState, SenderId = "SERVER", Data = JObject.FromObject(state2) });

            BroadcastRoomsList();
        }

        private async Task ClearStreamBufferAsync(NetworkStream stream)
        {
            try
            {
                if (stream != null && stream.CanRead && stream.DataAvailable)
                {
                    byte[] buffer = new byte[4096];
                    int totalRead = 0;
                    while (stream.DataAvailable)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                        totalRead += read;
                        await Task.Delay(1);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка очистки буфера: {ex.Message}");
            }
        }

        private async Task SkipRemainingDataAsync(NetworkStream stream)
        {
            try
            {
                if (stream.DataAvailable)
                {
                    byte[] buffer = new byte[4096];
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                }
            }
            catch { }
        }
    }
}
