﻿using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

class Program
{
    static IHubContext hubContext;

    static async Task Main(string[] args)
    {
        var (serverIP, port) = ConfigReader.GetServerConfig();
        if (string.IsNullOrEmpty(serverIP) || string.IsNullOrEmpty(port))
        {
            LogError("Invalid server IP or port. Check config.ini.");
            return;
        }

        // Validate if port is in a safe range
        if (!int.TryParse(port, out int portNumber) || portNumber < 1024)
        {
            LogError($"Port {port} is in the restricted range. Please use a port number above 1024.");
            return;
        }

        string url = $"http://{serverIP}:{port}";

        try
        {
            // Check if we can create a simple listener first
            var listener = new HttpListener();
            listener.Prefixes.Add(url + "/");
            listener.Start();
            listener.Stop();

            using (WebApp.Start(url))
            {
                hubContext = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();
                Console.WriteLine("Server started at " + url);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
        catch (HttpListenerException ex)
        {
            LogError($"Permission error: {ex.Message}. Try running as administrator or using a different port.");
        }
        catch (Exception ex)
        {
            var innerException = ex.InnerException?.Message ?? "No inner exception";
            LogError($"Error starting the SignalR server: {ex.Message}\nInner Exception: {innerException}\nStack Trace: {ex.StackTrace}");
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
