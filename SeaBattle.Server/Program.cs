using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SeaBattle.Shared.Models;

namespace SeaBattle.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "SeaBattle Server";
            Console.WriteLine("=== Морской бой - Сервер ===");

            try
            {
                var server = new GameServer();
                await server.StartAsync(8888);

                Console.WriteLine("Сервер запущен. Нажмите Enter для остановки...");
                Console.ReadLine();

                server.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.ReadKey();
            }
        }
    }
}