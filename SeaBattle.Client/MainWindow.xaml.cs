using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            App.Cts = new System.Threading.CancellationTokenSource();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverAddress = ServerAddressTextBox.Text.Trim();
                string playerName = PlayerNameTextBox.Text.Trim();

                if (string.IsNullOrEmpty(playerName))
                {
                    MessageBox.Show("Введите имя игрока", "Ошибка");
                    return;
                }

                ConnectButton.IsEnabled = false;
                ConnectButton.Content = "Подключение...";

                // Создаем подключение
                App.TcpClient = new TcpClient();
                await App.TcpClient.ConnectAsync(serverAddress, App.DefaultPort);
                App.Stream = App.TcpClient.GetStream();
                App.PlayerName = playerName;

                // Отправляем Connect
                var connectMsg = new NetworkMessage
                {
                    Type = MessageType.Connect,
                    Data = JObject.FromObject(new ConnectData { PlayerName = playerName })
                };
                await SendMessageAsync(connectMsg);

                // Ждем ответ с таймаутом
                var response = await ReadOneMessageWithTimeout(5000);

                if (response?.Type == MessageType.ConnectResponse)
                {
                    var data = response.Data.ToObject<ConnectResponseData>();
                    if (data.Success)
                    {
                        App.PlayerId = data.PlayerId;
                        App.PendingReconnectRoomId = data.PendingReconnectRoomId;
                        StatusText.Text = $"Подключено как {playerName}";
                        ConnectionStatus.Text = "Подключено";
                        ConnectionStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                        PlayerNameText.Text = playerName;

                        var lobbyPage = new LobbyPage();
                        this.Content = lobbyPage;
                        _ = Task.Run((Func<Task>)ReadLoop);
                        return;
                    }
                }

                // Если что-то пошло не так
                throw new Exception("Не удалось подключиться к серверу");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка");
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Подключиться";

                App.TcpClient?.Close();
                App.Stream?.Close();
            }
        }


        // Единственный цикл чтения сообщений из потока. Рассылает сообщения текущей странице (Lobby или Game), чтобы не было двух конкурирующих читателей и обрезанного JSON.

        private async Task ReadLoop()
        {
            try
            {
                while (!App.Cts.Token.IsCancellationRequested && App.TcpClient != null && App.TcpClient.Connected)
                {
                    if (App.Stream == null || !App.Stream.DataAvailable)
                    {
                        await Task.Delay(10);
                        continue;
                    }

                    byte[] lenBytes = new byte[4];
                    int read = await App.Stream.ReadAsync(lenBytes, 0, 4);
                    if (read < 4) continue;

                    int msgLen = BitConverter.ToInt32(lenBytes, 0);
                    if (msgLen <= 0 || msgLen > 10 * 1024 * 1024) continue;

                    byte[] msgData = new byte[msgLen];
                    int totalRead = 0;
                    while (totalRead < msgLen)
                    {
                        int r = await App.Stream.ReadAsync(msgData, totalRead, msgLen - totalRead);
                        if (r == 0) break;
                        totalRead += r;
                    }

                    if (totalRead != msgLen) continue;

                    string json = Encoding.UTF8.GetString(msgData);
                    var message = NetworkMessage.FromJson(json);
                    if (message == null) continue;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (Content is LobbyPage lobby)
                            lobby.HandleMessage(message);
                        else if (Content is GamePage game)
                            game.ProcessServerMessage(message);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadLoop: {ex.Message}");
            }
        }

        private async Task<NetworkMessage> ReadOneMessageWithTimeout(int timeoutMs)
        {
            try
            {
                var readTask = Task.Run(async () =>
                {
                    byte[] lenBytes = new byte[4];
                    int read = await App.Stream.ReadAsync(lenBytes, 0, 4);
                    if (read < 4) return null;

                    int msgLen = BitConverter.ToInt32(lenBytes, 0);
                    if (msgLen <= 0 || msgLen > 10 * 1024 * 1024) return null;

                    byte[] msgData = new byte[msgLen];
                    int totalRead = 0;
                    while (totalRead < msgLen)
                    {
                        int r = await App.Stream.ReadAsync(msgData, totalRead, msgLen - totalRead);
                        if (r == 0) return null;
                        totalRead += r;
                    }

                    string json = Encoding.UTF8.GetString(msgData);
                    return NetworkMessage.FromJson(json);
                });

                if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)) == readTask)
                {
                    return await readTask;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task SendMessageAsync(NetworkMessage message)
        {
            string json = message.ToJson();
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(data.Length);

            await App.Stream.WriteAsync(len, 0, 4);
            await App.Stream.WriteAsync(data, 0, data.Length);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            App.Cts.Cancel();
            App.Stream?.Close();
            App.TcpClient?.Close();
            Application.Current.Shutdown();
        }
    }
}