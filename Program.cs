using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;

namespace NotificationServerHost
{
    class Program
    {
        static IHubContext hubContext;

        static async Task Main(string[] args)
        {
            // Read the IP and port from config.ini (just as before)
            var (serverIP, port) = ConfigReader.GetServerConfig();

            if (string.IsNullOrEmpty(serverIP) || string.IsNullOrEmpty(port))
            {
                LogError("Invalid server IP or port. Check config.ini.");
                return;
            }

            // Construct the URL from the IP and port
            string url = $"http://{serverIP}:{port}";

            try
            {
                // Start the SignalR server
                using (WebApp.Start(url))
                {
                    // Set up SignalR context
                    hubContext = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();

                    Console.WriteLine("SignalR Server started at " + url);

                    // Periodically poll the NotificationLog and RequestNotificationLog tables for new notifications
                    await Task.WhenAll(PollNotificationLogAsync(), PollRequestNotificationLogAsync());

                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error starting the SignalR server: {ex.Message}");
            }
        }

        // Poll the NotificationLog table for new message notifications
        static async Task PollNotificationLogAsync()
        {
            string connectionString = "Data Source=192.168.1.114;Initial Catalog=Users;Trusted_Connection=True;MultipleActiveResultSets=True;";

            while (true)
            {
                try
                {
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        connection.Open();

                        // Query for new notifications from the NotificationLog table
                        string query = "SELECT LogID, MessageID, SenderID, ReceiverID, MessageText FROM NotificationLog WHERE IsNotified = 0";
                        SqlCommand command = new SqlCommand(query, connection);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int logID = reader.GetInt32(reader.GetOrdinal("LogID"));
                                int messageID = reader.GetInt32(reader.GetOrdinal("MessageID"));
                                int senderID = reader.GetInt32(reader.GetOrdinal("SenderID"));
                                int receiverID = reader.GetInt32(reader.GetOrdinal("ReceiverID"));
                                string messageText = reader.GetString(reader.GetOrdinal("MessageText"));

                                // Send the message notification to the specific user (using ReceiverID)
                                await NotifyClients(messageText, receiverID);

                                // Mark the message as notified
                                await MarkAsNotified(connection, logID);
                            }
                        }
                    }

                    // Wait for a while before checking again (e.g., 5 seconds)
                    await Task.Delay(5000);
                }
                catch (Exception ex)
                {
                    LogError($"Error during polling NotificationLog: {ex.Message}");
                }
            }
        }

        // Poll the RequestNotificationLog table for new request notifications
        static async Task PollRequestNotificationLogAsync()
        {
            string connectionString = "Data Source=192.168.1.114;Initial Catalog=Users;Trusted_Connection=True;MultipleActiveResultSets=True;";

            while (true)
            {
                try
                {
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        connection.Open();

                        // Query for new request notifications from the RequestNotificationLog table
                        string query = "SELECT NotificationID, RequestID, SenderID, ReceiverID, RequestReason FROM RequestNotificationLog WHERE IsNotified = 0";
                        SqlCommand command = new SqlCommand(query, connection);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int NotificationID = reader.GetInt32(reader.GetOrdinal("NotificationID"));
                                int requestID = reader.GetInt32(reader.GetOrdinal("RequestID"));
                                int senderID = reader.GetInt32(reader.GetOrdinal("SenderID"));
                                int receiverID = reader.GetInt32(reader.GetOrdinal("ReceiverID"));
                                string requestReason = reader.GetString(reader.GetOrdinal("RequestReason"));

                                // Send the request notification to the specific user (using ReceiverID)
                                await NotifyClients(requestReason, receiverID);

                                // Mark the request notification as notified
                                await MarkAsNotified(connection, NotificationID);
                            }
                        }
                    }

                    // Wait for a while before checking again (e.g., 5 seconds)
                    await Task.Delay(5000);
                }
                catch (Exception ex)
                {
                    LogError($"Error during polling RequestNotificationLog: {ex.Message}");
                }
            }
        }

        // Send the notification to a specific user using SignalR
        static async Task NotifyClients(string message, int receiverID)
        {
            try
            {
                await Task.Run(() =>
                {
                    // Assuming you use receiverID to get a specific user (perhaps from a connected users list)
                    hubContext.Clients.User(receiverID.ToString()).receiveNotification(message);
                });
            }
            catch (Exception ex)
            {
                LogError($"Error sending notification to user {receiverID}: {ex.Message}");
            }
        }

        // Mark the notification as "notified"
        static async Task<bool> MarkAsNotified(SqlConnection connection, int logID)
        {
            try
            {
                string updateQuery = "UPDATE RequestNotificationLog SET IsNotified = 1 WHERE LogID = @LogID";
                SqlCommand command = new SqlCommand(updateQuery, connection);
                command.Parameters.AddWithValue("@LogID", logID);

                // Execute the update query asynchronously and check the number of affected rows
                int rowsAffected = await command.ExecuteNonQueryAsync();

                // If at least one row is affected, the message was marked as read
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"Error marking notification {logID} as read: {ex.Message}");
                return false;
            }
        }

        // Log only critical errors that crash the server
        static void LogError(string message)
        {
            try
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                using (StreamWriter writer = new StreamWriter(logFilePath, true))
                {
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] : {message}");
                }
            }
            catch (Exception ex)
            {
                // Log failure to log the error (just in case the log file writing fails)
                Console.WriteLine($"Error logging message: {ex.Message}");
            }
        }
    }
}
