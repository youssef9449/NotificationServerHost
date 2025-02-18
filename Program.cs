using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;

namespace NotificationServerHost
{
    class Program
    {
        static IHubContext hubContext;
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
        private static CancellationTokenSource _cancellationTokenSource;
        public static string connectionString = "Data Source=192.168.1.9;Initial Catalog=Users;User ID=sa;Password=123;MultipleActiveResultSets=True;";

        static async Task Main(string[] args)
        {
            var (serverIP, port) = ConfigReader.GetServerConfig();
            if (string.IsNullOrEmpty(serverIP) || string.IsNullOrEmpty(port))
            {
                LogError("Invalid server IP or port. Check config.ini.");
                return;
            }

            string url = $"http://{serverIP}:{port}";
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Start the SignalR server
                using (WebApp.Start(url))
                {
                    hubContext = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();
                    Console.WriteLine("SignalR Server started at " + url);

                    // Start polling tasks for notifications
                    Task pollNotificationTask = PollNotificationLogAsync(_cancellationTokenSource.Token);
                    Task pollRequestNotificationTask = PollRequestNotificationLogAsync(_cancellationTokenSource.Token);

                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();

                    _cancellationTokenSource.Cancel();
                    try
                    {
                        await Task.WhenAll(pollNotificationTask, pollRequestNotificationTask);
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("Tasks canceled gracefully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error starting the SignalR server: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
            }
        }

        // Polls the Notifications table for new messages.
        static async Task PollNotificationLogAsync(CancellationToken cancellationToken)
        {
            int errorBackoffMs = 1000;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync(cancellationToken);
                        string query = "SELECT NotificationID, MessageID, SenderID, ReceiverID, MessageText FROM Notifications WHERE IsSeen = 0";
                        SqlCommand command = new SqlCommand(query, connection);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                int notificationID = reader.GetInt32(reader.GetOrdinal("NotificationID"));
                                int receiverID = reader.GetInt32(reader.GetOrdinal("ReceiverID"));
                                string messageText = reader.GetString(reader.GetOrdinal("MessageText"));

                                bool notificationSent = await NotifyClients(messageText, receiverID);
                                if (notificationSent)
                                {
                                    bool marked = await MarkNotificationAsNotified(connection, notificationID, "Notifications");
                                    if (!marked)
                                    {
                                        LogError($"Failed to mark notification ID {notificationID} as notified.");
                                    }
                                }
                            }
                        }
                    }
                    errorBackoffMs = 1000;
                    await Task.Delay(PollingInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Error during polling NotificationLog: {ex.Message}");
                    await Task.Delay(errorBackoffMs, CancellationToken.None);
                    errorBackoffMs = Math.Min(errorBackoffMs * 2, 60000);
                }
            }
        }

        // Polls the RequestNotificationLog table for new requests.
        static async Task PollRequestNotificationLogAsync(CancellationToken cancellationToken)
        {
            int errorBackoffMs = 1000;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync(cancellationToken);
                        string query = "SELECT NotificationID, RequestID, SenderID, ReceiverID, RequestReason FROM RequestNotificationLog WHERE IsSeen = 0";
                        SqlCommand command = new SqlCommand(query, connection);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                int notificationID = reader.GetInt32(reader.GetOrdinal("NotificationID"));
                                int receiverID = reader.GetInt32(reader.GetOrdinal("ReceiverID"));
                                string requestReason = reader.GetString(reader.GetOrdinal("RequestReason"));

                                bool notificationSent = await NotifyClients(requestReason, receiverID);
                                if (notificationSent)
                                {
                                    bool marked = await MarkNotificationAsNotified(connection, notificationID, "RequestNotificationLog");
                                    if (!marked)
                                    {
                                        LogError($"Failed to mark request notification ID {notificationID} as notified.");
                                    }
                                }
                            }
                        }
                    }
                    errorBackoffMs = 1000;
                    await Task.Delay(PollingInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError($"Error during polling RequestNotificationLog: {ex.Message}");
                    await Task.Delay(errorBackoffMs, CancellationToken.None);
                    errorBackoffMs = Math.Min(errorBackoffMs * 2, 60000);
                }
            }
        }

        // Uses the hub context to send a notification to a specific user.
        static async Task<bool> NotifyClients(string message, int receiverID)
        {
            try
            {
                var connectionIDs = NotificationHub.GetConnectionIDsByUserID(receiverID);
                if (connectionIDs != null && connectionIDs.Count > 0)
                {
                    await Task.Run(() =>
                    {
                        hubContext.Clients.Clients(connectionIDs).receiveNotification(message);
                    });
                    return true;
                }
                else
                {
                    Console.WriteLine($"User {receiverID} is not connected. Message queued for later delivery.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"Error sending notification to user {receiverID}: {ex.Message}");
                return false;
            }
        }

        // Marks a notification as seen in the database.
        static async Task<bool> MarkNotificationAsNotified(SqlConnection connection, int notificationID, string tableName)
        {
            try
            {
                string columnName = tableName == "Notifications" ? "NotificationID" : "RequestID";
                string updateQuery = $"UPDATE {tableName} SET IsSeen = 1 WHERE {columnName} = @NotificationID";
                SqlCommand command = new SqlCommand(updateQuery, connection);
                command.Parameters.AddWithValue("@NotificationID", notificationID);
                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"Error marking notification {notificationID} as notified in {tableName}: {ex.Message}");
                return false;
            }
        }

        // Basic error logging.
        static void LogError(string message)
        {
            try
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                using (StreamWriter writer = new StreamWriter(logFilePath, true))
                {
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] : {message}");
                }
                Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] : {message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging message: {ex.Message}");
            }
        }
    }
}
