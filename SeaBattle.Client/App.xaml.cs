using System.Windows;

namespace SeaBattle.Client
{
    public partial class App : Application
    {
        private MainWindow _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mainWindow = new MainWindow();

            // Создаем страницу подключения и передаем ссылку на главное окно
            var connectPage = new ConnectPage(_mainWindow);

            // Устанавливаем начальную страницу
            _mainWindow.MainFrame.Navigate(connectPage);

            _mainWindow.Show();
        }
    }
}