using System;
using System.Windows;
using System.Windows.Controls;

namespace SeaBattle.Client
{
    public partial class ConnectPage : Page
    {
        private MainWindow _mainWindow;

        public ConnectPage()
        {
            InitializeComponent();
        }

        public ConnectPage(MainWindow mainWindow) : this()
        {
            _mainWindow = mainWindow;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverAddress = ServerAddressTextBox.Text;
                int port = int.Parse(PortTextBox.Text);
                string playerName = PlayerNameTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(playerName))
                {
                    MessageBox.Show("Введите имя игрока", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show($"Попытка подключения к {serverAddress}:{port} как {playerName}", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);

                // Пока просто переходим в лобби для тестирования
                _mainWindow.UpdatePlayerName(playerName);
                _mainWindow.UpdateStatus("Подключено (тестовый режим)");

                var lobbyPage = new LobbyPage(_mainWindow);
                _mainWindow.NavigateToPage(lobbyPage);
            }
            catch (FormatException)
            {
                MessageBox.Show("Неверный формат порта", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}