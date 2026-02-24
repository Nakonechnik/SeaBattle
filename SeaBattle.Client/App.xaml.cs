using System.Net.Sockets;
using System.Threading;
using System.Windows;

namespace SeaBattle.Client
{
    public partial class App : Application
    {
        public static TcpClient TcpClient { get; set; }
        public static NetworkStream Stream { get; set; }
        public static string PlayerId { get; set; }
        public static string PlayerName { get; set; }
        public static string PendingReconnectRoomId { get; set; }
        public static CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
    }
}