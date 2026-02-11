using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SeaBattle.Client
{
    public partial class MainWindow : Window
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private bool _isConnected;
        private CancellationTokenSource _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            _isConnected = false;
            UpdateUI();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverAddress = ServerAddressTextBox.Text;
                int port = int.Parse(PortTextBox.Text);

                AddMessage($"Подключение к {serverAddress}:{port}...");

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(serverAddress, port);

                _stream = _tcpClient.GetStream();
                _isConnected = true;
                _cancellationTokenSource = new CancellationTokenSource();

                AddMessage("Успешно подключено!");
                UpdateUI();

                // Запускаем прослушивание сообщений от сервера
                _ = Task.Run(() => ListenToServer(_cancellationTokenSource.Token));

                // Читаем приветственное сообщение от сервера
                await ReadWelcomeMessage();
            }
            catch (FormatException)
            {
                AddMessage("Ошибка: неверный формат порта");
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка подключения: {ex.Message}");
            }
        }

        private async Task ReadWelcomeMessage()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                AddMessage($"Сервер: {message}");
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка чтения приветствия: {ex.Message}");
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected || _tcpClient == null)
            {
                AddMessage("Сначала подключитесь к серверу");
                return;
            }

            string message = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message))
            {
                AddMessage("Введите сообщение для отправки");
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");

                await _stream.WriteAsync(data, 0, data.Length);
                AddMessage($"Вы: {message}");
                MessageTextBox.Text = "";
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка отправки: {ex.Message}");
                Disconnect();
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            try
            {
                _isConnected = false;

                // Отменяем задачу прослушивания
                _cancellationTokenSource?.Cancel();

                // Закрываем соединение
                _stream?.Close();
                _tcpClient?.Close();

                AddMessage("Отключено от сервера");
                UpdateUI();
            }
            catch (Exception ex)
            {
                AddMessage($"Ошибка отключения: {ex.Message}");
            }
            finally
            {
                _stream = null;
                _tcpClient = null;
            }
        }

        private async Task ListenToServer(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[1024];

                while (_isConnected && _tcpClient?.Connected == true)
                {
                    // Используем CancellationToken для корректного прерывания
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        // Асинхронное чтение с таймаутом
                        var readTask = _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                        // Ждем с таймаутом, чтобы не зависнуть
                        if (await Task.WhenAny(readTask, Task.Delay(1000, cancellationToken)) == readTask)
                        {
                            int bytesRead = await readTask;
                            if (bytesRead == 0) break;

                            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                            Dispatcher.Invoke(() =>
                            {
                                AddMessage($"Сервер: {message}");
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Задача отменена - нормальный выход
                        break;
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                    {
                        // Соединение закрыто
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddMessage($"Ошибка соединения: {ex.Message}");
                        Disconnect();
                    });
                }
            }
        }

        private void AddMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            MessageTextBlock.Text += $"[{timestamp}] {message}\n";

            // Прокручиваем вниз
            var scrollViewer = GetChildOfType<ScrollViewer>(MessageTextBlock.Parent as DependencyObject);
            scrollViewer?.ScrollToEnd();
        }

        private T GetChildOfType<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);

                var result = (child as T) ?? GetChildOfType<T>(child);
                if (result != null) return result;
            }

            return null;
        }

        private void UpdateUI()
        {
            ConnectButton.IsEnabled = !_isConnected;
            SendButton.IsEnabled = _isConnected;
            DisconnectButton.IsEnabled = _isConnected;
            ServerAddressTextBox.IsEnabled = !_isConnected;
            PortTextBox.IsEnabled = !_isConnected;
            MessageTextBox.IsEnabled = _isConnected;

            StatusText.Text = _isConnected ? "Подключено" : "Не подключено";
            StatusText.Foreground = _isConnected ?
                System.Windows.Media.Brushes.LightGreen :
                System.Windows.Media.Brushes.LightCoral;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Disconnect();
            base.OnClosing(e);
        }
    }
}