using System.Windows;
using System.Windows.Controls;

namespace SeaBattle.Client
{
    public partial class LobbyPage : Page
    {
        private MainWindow _mainWindow;

        public LobbyPage()
        {
            InitializeComponent();
        }

        public LobbyPage(MainWindow mainWindow) : this()
        {
            _mainWindow = mainWindow;
            UpdateWelcomeText();
        }

        private void UpdateWelcomeText()
        {
            WelcomeText.Text = $"Добро пожаловать, {_mainWindow?.PlayerNameText.Text ?? "Игрок"}!";
        }

        private void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Комната создана (тестовый режим)", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Ожидаем второго игрока...";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var connectPage = new ConnectPage(_mainWindow);
            _mainWindow.NavigateToPage(connectPage);
        }
    }
}