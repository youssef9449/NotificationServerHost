using System;
using System.IO;

public static class ConfigReader
{
    public static (string serverIP, string port) GetServerConfig()
    {
        try
        {
            // Look for config.ini in the executable directory
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (File.Exists(configFilePath))
            {
                string[] lines = File.ReadAllLines(configFilePath);
                string serverIP = null;
                string port = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("ServerIP", StringComparison.OrdinalIgnoreCase))
                    {
                        serverIP = line.Split('=')[1].Trim();
                    }
                    else if (line.StartsWith("Port", StringComparison.OrdinalIgnoreCase))
                    {
                        port = line.Split('=')[1].Trim();
                    }
                }

                if (!string.IsNullOrEmpty(serverIP) && !string.IsNullOrEmpty(port))
                {
                    return (serverIP, port);
                }
                else
                {
                    throw new Exception("Invalid ServerIP or Port in config.ini.");
                }
            }
            else
            {
                throw new FileNotFoundException("config.ini not found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading INI file: {ex.Message}");
            return (null, null);
        }
    }
}
