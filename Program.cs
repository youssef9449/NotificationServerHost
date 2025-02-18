using System;
using System.IO;

class Program
{
    static void Main()
    {
        // Path to the config.ini file in the same directory as the executable
        string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

        // Read the server IP from the INI file
        string serverIP = GetServerIP(configFilePath);

        if (string.IsNullOrEmpty(serverIP))
        {
            Console.WriteLine("Server IP not found or invalid.");
            return;
        }

        // Construct the SignalR URL with the server IP
        string url = $"http://{serverIP}:8080";

        Console.WriteLine($"SignalR server URL: {url}");
        Console.Write("Press any key to exit....");
        Console.ReadKey();
        // Continue with your SignalR connection logic here
    }

    // Method to get ServerIP from the config.ini file
    static string GetServerIP(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string[] lines = File.ReadAllLines(filePath);

                foreach (var line in lines)
                {
                    if (line.StartsWith("ServerIP", StringComparison.OrdinalIgnoreCase))
                    {
                        // Get the IP address from the line
                        return line.Split('=')[1].Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading INI file: {ex.Message}");
        }

        return null;
    }
}
