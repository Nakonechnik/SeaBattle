using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;
using SeaBattle.Server.Models;

namespace SeaBattle.Server
{
    public partial class GameServer
    {
        private void CancelTurnTimer(string roomId)
        {
            if (_turnTimers.TryRemove(roomId, out var cts))
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }
            _turnStartedAt.TryRemove(roomId, out _);
        }

        private int GetTimeLeftForRoom(string roomId)
        {
            if (!_turnStartedAt.TryGetValue(roomId, out var startedAt))
                return GameConstants.TurnTimeSeconds;
            var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
            var left = GameConstants.TurnTimeSeconds - (int)elapsed;
            return Math.Max(0, Math.Min(left, GameConstants.TurnTimeSeconds));
        }

        private void StartTurnTimer(string roomId)
        {
            CancelTurnTimer(roomId);
            var cts = new CancellationTokenSource();
            if (!_turnTimers.TryAdd(roomId, cts))
            {
                cts.Dispose();
                return;
            }
            _turnStartedAt[roomId] = DateTime.UtcNow;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(GameConstants.TurnTimeSeconds), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var room = _lobbyManager.GetRoom(roomId);
                if (room == null) return;
                if (!_gameSessions.TryGetValue(roomId, out var gameSession) || gameSession.Status != GameSessionStatus.InProgress)
                    return;
                if (!_turnTimers.TryRemove(roomId, out _))
                    return;
                _turnStartedAt.TryRemove(roomId, out _);

                var loserId = gameSession.CurrentTurnPlayerId;
                var opponent = room.GetOpponent(loserId);
                if (opponent == null) return;

                gameSession.Status = GameSessionStatus.Finished;
                var loser = loserId == room.Creator.Id ? room.Creator : room.Player2;
                var winner = opponent;

                var gameOverData = new GameOverData
                {
                    WinnerId = winner.Id,
                    WinnerName = winner.Name,
                    LoserId = loser.Id,
                    LoserName = loser.Name,
                    IsSurrender = false,
                    IsTimeout = true
                };

                if (_playerStreams.TryGetValue(winner.Id, out var winnerStream))
                    await SendMessageAsync(winnerStream, new NetworkMessage { Type = MessageType.GameOver, SenderId = "SERVER", Data = JObject.FromObject(gameOverData) });
                if (_playerStreams.TryGetValue(loser.Id, out var loserStream))
                    await SendMessageAsync(loserStream, new NetworkMessage { Type = MessageType.GameOver, SenderId = "SERVER", Data = JObject.FromObject(gameOverData) });

                _lobbyManager.RemoveRoom(roomId);
                _gameSessions.TryRemove(roomId, out _);
                BroadcastRoomsList();
            });
        }
    }
}
