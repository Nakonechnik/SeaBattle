using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SeaBattle.Client
{
    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected;

        public MainWindow()
        {
            InitializeComponent();
            UpdateStatus("Не подключено");
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectToServer();
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            DisconnectFromServer();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private async void ConnectToServer()
        {
            try
            {
                string server = ServerTextBox.Text;
                int port = int.Parse(PortTextBox.Text);

                UpdateStatus($"Подключение к {server}:{port}...");

                _client = new TcpClient();
                await _client.ConnectAsync(server, port);

                _stream = _client.GetStream();
                _isConnected = true;

                UpdateStatus($"Подключено к {server}:{port}");
                AddLog("Подключение установлено");

                // Включаем кнопки
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
                SendButton.IsEnabled = true;

                // Запускаем получение сообщений
                Task.Run(() => ReceiveMessages());
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка подключения: {ex.Message}");
                AddLog($"Ошибка: {ex.Message}");
            }
        }

        private void DisconnectFromServer()
        {
            try
            {
                _isConnected = false;

                if (_stream != null)
                {
                    // Отправляем сообщение о выходе
                    byte[] data = Encoding.UTF8.GetBytes("exit");
                    _stream.Write(data, 0, data.Length);

                    _stream.Close();
                    _stream = null;
                }

                if (_client != null)
                {
                    _client.Close();
                    _client = null;
                }

                UpdateStatus("Отключено");
                AddLog("Соединение разорвано");

                // Обновляем кнопки
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                SendButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка отключения: {ex.Message}");
                AddLog($"Ошибка: {ex.Message}");
            }
        }

        private async void SendMessage()
        {
            if (!_isConnected || _stream == null)
            {
                AddLog("Нет подключения к серверу");
                return;
            }

            string message = MessageTextBox.Text;
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                await _stream.WriteAsync(data, 0, data.Length);

                AddLog($"Отправлено: {message}");
                MessageTextBox.Text = ""; // Очищаем поле
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка отправки: {ex.Message}");
                DisconnectFromServer();
            }
        }

        private async Task ReceiveMessages()
        {
            byte[] buffer = new byte[1024];

            while (_isConnected && _client != null && _client.Connected)
            {
                try
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // Сервер отключился
                        Dispatcher.Invoke(() =>
                        {
                            AddLog("Сервер отключился");
                            DisconnectFromServer();
                        });
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    Dispatcher.Invoke(() =>
                    {
                        AddLog($"Получено: {message}");
                    });
                }
                catch (Exception)
                {
                    // Ошибка чтения - вероятно, соединение разорвано
                    Dispatcher.Invoke(() =>
                    {
                        AddLog("Ошибка соединения");
                        DisconnectFromServer();
                    });
                    break;
                }
            }
        }

        private void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        private void AddLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogTextBox.Text += $"[{timestamp}] {message}\n";
                LogTextBox.ScrollToEnd();
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            DisconnectFromServer();
            base.OnClosing(e);
        }
    }
}