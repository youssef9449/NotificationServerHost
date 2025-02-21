using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Threading;
using System;

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
            using (WebApp.Start(url))
            {
                hubContext = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();
                Console.WriteLine("SignalR Server started at " + url);

                Task pollNotificationTask = PollTableAsync("Notifications", "NotificationID", "MessageText", _cancellationTokenSource.Token);
               // Task pollRequestNotificationTask = PollTableAsync("RequestNotificationLog", "RequestID", "RequestReason", _cancellationTokenSource.Token);

               // Console.WriteLine("Press any key to exit...");
                Console.ReadKey();

                _cancellationTokenSource.Cancel();
                await Task.WhenAll(pollNotificationTask);
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

    static async Task PollTableAsync(string tableName, string idColumn, string messageColumn, CancellationToken cancellationToken)
    {
        int errorBackoffMs = 1000;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    string query = $"SELECT NotificationID, SenderID, ReceiverID, {messageColumn} FROM {tableName} WHERE IsSeen = 0";
                    SqlCommand command = new SqlCommand(query, connection);

                    using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            int notificationID = reader.GetInt32(reader.GetOrdinal("NotificationID"));
                            int receiverID = reader.GetInt32(reader.GetOrdinal("ReceiverID"));
                            string messageText = reader.GetString(reader.GetOrdinal(messageColumn));
                            int? senderID = reader.IsDBNull(reader.GetOrdinal("SenderID")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("SenderID"));

                            bool notificationSent = await NotifyGeneralClients(messageText, receiverID);
                            if (notificationSent)
                            {
                                bool marked = await MarkNotificationAsSeen(connection, notificationID, tableName, idColumn);
                                if (!marked)
                                {
                                    LogError($"Failed to mark notification ID {notificationID} as seen in {tableName}.");
                                }
                            }
                        }
                    }
                }
                errorBackoffMs = 1000;
                await Task.Delay(PollingInterval, cancellationToken);
            }
            catch (Exception ex)
            {
                LogError($"Error during polling {tableName}: {ex.Message}");
                await Task.Delay(errorBackoffMs, CancellationToken.None);
                errorBackoffMs = Math.Min(errorBackoffMs * 2, 60000);
            }
        }
    }

    static async Task<bool> NotifyGeneralClients(string message, int receiverID)
    {
        try
        {
            var connectionIDs = NotificationHub.GetConnectionIDsByUserID(receiverID);
            if (connectionIDs != null && connectionIDs.Count > 0)
            {
                await Task.Run(() =>
                {
                    hubContext.Clients.Clients(connectionIDs).receiveGeneralNotification(message);
                    Console.WriteLine($"Sent general notification to {receiverID}: {message}");
                });
                return true;
            }
            else
            {
                Console.WriteLine($"User {receiverID} is not connected. Notification queued.");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error sending notification to user {receiverID}: {ex.Message}");
            return false;
        }
    }

    static async Task<bool> MarkNotificationAsSeen(SqlConnection connection, int notificationID, string tableName, string idColumn)
    {
        try
        {
            string updateQuery = $"UPDATE {tableName} SET IsSeen = 1 WHERE {idColumn} = @NotificationID";
            using (SqlCommand command = new SqlCommand(updateQuery, connection))
            {
                command.Parameters.AddWithValue("@NotificationID", notificationID);
                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error marking notification {notificationID} as seen in {tableName}: {ex.Message}");
            return false;
        }
    }

    static void LogError(string message)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] : {message}");
    }
}