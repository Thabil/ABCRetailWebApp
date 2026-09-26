using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using System.Text;
using System.Text.Json;
using ABCRetailWebApp.Functions.Models;

namespace ABCRetailWebApp.Functions.Functions
{
    /// <summary>
    /// Consumes stock alert events from stock-alerts-queue.
    /// 
    /// Operations:
    /// 1. Writes alert record to StockAlerts table
    /// 2. Generates and uploads CSV summary to blob storage (reports container)
    /// 3. Publishes transaction event to transaction-queue for centralized logging
    /// 
    /// Trigger: Queue message in 'stock-alerts-queue'
    /// Output: StockAlerts table, reports blob container, transaction-queue
    /// 
    /// This function demonstrates multi-output scenario and blob operations.
    /// </summary>
    public class StockAlertsFunction
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger _logger;

        public StockAlertsFunction(
            TableServiceClient tableServiceClient,
            BlobServiceClient blobServiceClient,
            QueueServiceClient queueServiceClient,
            ILoggerFactory loggerFactory)
        {
            _tableServiceClient = tableServiceClient;
            _blobServiceClient = blobServiceClient;
            _queueServiceClient = queueServiceClient;
            _logger = loggerFactory.CreateLogger<StockAlertsFunction>();
        }

        [Function(nameof(StockAlertsFunction))]
        public async Task Run(
            [QueueTrigger("stock-alerts-queue")] string queueItem,
            FunctionContext context)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                _logger.LogInformation($"StockAlerts function triggered. Message: {queueItem}");

                // Parse the stock alert event
                var alertEvent = JsonSerializer.Deserialize<StockAlertEvent>(queueItem)
                    ?? throw new InvalidOperationException("Failed to deserialize stock alert event");

                // Step 1: Write to StockAlerts table
                var tableClient = _tableServiceClient.GetTableClient("StockAlerts");
                await tableClient.CreateIfNotExistsAsync();

                var stockAlert = new StockAlertEntity
                {
                    PartitionKey = alertEvent.ProductId,
                    RowKey = alertEvent.ProductId,  // stable key — one row per product, upserted on each alert
                    ProductId = alertEvent.ProductId,
                    ProductName = alertEvent.ProductName,
                    Category = alertEvent.Category,
                    RemainingStock = alertEvent.RemainingStock,
                    LastAlertTime = DateTime.SpecifyKind(alertEvent.Timestamp, DateTimeKind.Utc)
                };

                await tableClient.UpsertEntityAsync(stockAlert, TableUpdateMode.Replace);
                _logger.LogInformation($"Stock alert saved to table. ProductId: {alertEvent.ProductId}");

                // Step 2: Generate and upload CSV report to blob
                await GenerateAndUploadStockAlertReport(alertEvent);

                // Step 3: Publish transaction event for centralized logging
                await PublishTransactionEvent(alertEvent, startTime);

                _logger.LogInformation($"StockAlerts function completed successfully");
            }
            catch (JsonException ex)
            {
                _logger.LogError($"JSON deserialization error: {ex.Message}. Message: {queueItem}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in StockAlerts function: {ex.Message}. Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task GenerateAndUploadStockAlertReport(StockAlertEvent alertEvent)
        {
            try
            {
                // Get or create blob container
                var containerClient = _blobServiceClient.GetBlobContainerClient("reports");
                await containerClient.CreateIfNotExistsAsync();

                // Query all stock alerts for today
                var tableClient = _tableServiceClient.GetTableClient("StockAlerts");
                var todayStart = DateTime.UtcNow.Date;

                var alerts = await tableClient.QueryAsync<StockAlertEntity>(
                    filter: (string)null!,
                    maxPerPage: 1000
                ).ToListAsync();

                // Filter for today's alerts
                var todaysAlerts = alerts
                    .Where(a => a.LastAlertTime.Date == todayStart)
                    .OrderByDescending(a => a.LastAlertTime)
                    .ToList();

                // Generate CSV
                var csv = new StringBuilder();
                csv.AppendLine("ProductId,ProductName,Category,RemainingStock,AlertTime");

                foreach (var alert in todaysAlerts)
                {
                    csv.AppendLine($"\"{alert.ProductId}\",\"{alert.ProductName}\",\"{alert.Category}\",{alert.RemainingStock},\"{alert.LastAlertTime.ToString("yyyy-MM-dd HH:mm:ss")}\"");
                }

                // Upload to blob
                var blobName = $"stock-alerts-{DateTime.UtcNow:yyyy-MM-dd}.csv";
                var blobClient = containerClient.GetBlobClient(blobName);

                using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
                await blobClient.UploadAsync(memoryStream, overwrite: true);

                _logger.LogInformation($"Stock alert report uploaded to blob: {blobName} ({todaysAlerts.Count} records)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating stock alert report: {ex.Message}");
                // Don't rethrow - let the main flow continue even if report generation fails
            }
        }

        private async Task PublishTransactionEvent(StockAlertEvent alertEvent, DateTime startTime)
        {
            try
            {
                var transactionEvent = new TransactionEvent
                {
                    EventType = "StockAlertProcessed",
                    OrderId = alertEvent.ProductId, // ProductId used as correlation key for stock events
                    Timestamp = DateTime.UtcNow,
                    Details = new Dictionary<string, object>
                    {
                        { "ProductName", alertEvent.ProductName },
                        { "ProductId", alertEvent.ProductId },
                        { "Category", alertEvent.Category },
                        { "RemainingStock", alertEvent.RemainingStock }
                    }
                };

                var queueClient = _queueServiceClient.GetQueueClient("transaction-queue");
                await queueClient.CreateIfNotExistsAsync();

                var messageJson = JsonSerializer.Serialize(transactionEvent);
                await queueClient.SendMessageAsync(messageJson);

                _logger.LogInformation($"Transaction event published to queue");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error publishing transaction event: {ex.Message}");
                throw;
            }
        }
    }

    // Helper extension for IAsyncEnumerable
    internal static class AsyncEnumerableExtensions
    {
        public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
        {
            var list = new List<T>();
            await foreach (var item in source)
            {
                list.Add(item);
            }
            return list;
        }
    }
}
