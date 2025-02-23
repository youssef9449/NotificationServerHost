using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    static IHubContext hubContext;
    //public static string connectionString = "Data Source=192.168.1.9;Initial Catalog=Users;User ID=sa;Password=123;MultipleActiveResultSets=True;";
    public static string connectionString = "Data Source=192.168.1.11;Initial Catalog=Users;Trusted_Connection=True;";

    static async Task Main(string[] args)
    {
        var (serverIP, port) = ConfigReader.GetServerConfig();
        if (string.IsNullOrEmpty(serverIP) || string.IsNullOrEmpty(port))
        {
            LogError("Invalid server IP or port. Check config.ini.");
            return;
        }

        string url = $"http://{serverIP}:{port}";

        try
        {
            using (WebApp.Start(url))
            {
                hubContext = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();
                Console.WriteLine("Server started at " + url);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            LogError($"Error starting the SignalR server: {ex.Message}");
        }
    }

    static void LogError(string message)
    {
        string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.ini");
        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] : {message}{Environment.NewLine}";
        try
        {
            // Append the error message to log.ini. The file is created if it doesn't exist.
            File.AppendAllText(logFilePath, logMessage);
        }
        catch (Exception ex)
        {
            // If logging fails, write the error to the console.
            Console.WriteLine("Failed to write log: " + ex.Message);
        }
        Console.WriteLine(logMessage);
    }
}
