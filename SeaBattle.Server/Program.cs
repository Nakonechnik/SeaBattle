using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SeaBattle.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "SeaBattle Server";
            Console.WriteLine("=== Морской бой - Сервер ===");

            try
            {
                var server = new TcpListener(IPAddress.Any, 8888);
                server.Start();

                Console.WriteLine("Сервер запущен на порту 8888");
                Console.WriteLine("Ожидание подключений...");
                Console.WriteLine("Нажмите Ctrl+C для остановки");

                while (true)
                {
                    var client = server.AcceptTcpClient();
                    Console.WriteLine($"Новое подключение от {client.Client.RemoteEndPoint}");

                    // Обрабатываем клиента в отдельном потоке
                    System.Threading.Tasks.Task.Run(() => HandleClient(client));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.ReadKey();
            }
        }

        static void HandleClient(TcpClient client)
        {
            string clientInfo = client.Client.RemoteEndPoint?.ToString() ?? "неизвестный клиент";

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    // Отправляем приветствие
                    string welcome = $"Добро пожаловать на сервер Морского боя, {clientInfo}!\n";
                    byte[] welcomeData = Encoding.UTF8.GetBytes(welcome);
                    stream.Write(welcomeData, 0, welcomeData.Length);

                    Console.WriteLine($"Отправлено приветствие клиенту {clientInfo}");

                    // Читаем сообщения от клиента
                    byte[] buffer = new byte[1024];

                    while (client.Connected)
                    {
                        if (!stream.DataAvailable)
                        {
                            System.Threading.Thread.Sleep(100); // Небольшая пауза
                            continue;
                        }

                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Console.WriteLine($"{clientInfo} сказал: {message}");

                        // Отправляем эхо
                        string echo = $"Эхо от сервера: {message}\n";
                        byte[] echoData = Encoding.UTF8.GetBytes(echo);
                        stream.Write(echoData, 0, echoData.Length);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                Console.WriteLine($"Клиент {clientInfo} отключился");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента {clientInfo}: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"Соединение с {clientInfo} закрыто");
            }
        }
    }
}