using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SeaBattle.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Морской бой - Сервер";
            Console.WriteLine("=== Морской бой - Сервер ===");
            Console.WriteLine("Запуск сервера...\n");

            try
            {
                // Создаем TCP сервер на порту 8888
                TcpListener server = new TcpListener(IPAddress.Any, 8888);
                server.Start();

                Console.WriteLine($"Сервер запущен на {IPAddress.Any}:8888");
                Console.WriteLine("Ожидание подключений...\n");

                // Бесконечный цикл для принятия подключений
                while (true)
                {
                    // Принимаем подключение
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine($"Новое подключение: {client.Client.RemoteEndPoint}");

                    // Обрабатываем клиента в отдельном потоке
                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient));
                    clientThread.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.ReadKey();
            }
        }

        static void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();

            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    // Преобразуем байты в строку
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Получено: {message}");

                    // Отправляем эхо-ответ
                    byte[] response = Encoding.UTF8.GetBytes($"Эхо: {message}");
                    stream.Write(response, 0, response.Length);

                    // Если клиент отправляет "exit", закрываем соединение
                    if (message.ToLower() == "exit")
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("Клиент отключился");
                stream.Close();
                client.Close();
            }
        }
    }
}