using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure.Storage.Files.Shares;
using System.Text.Json;
using ABCRetailWebApp.Functions.Models;

namespace ABCRetailWebApp.Functions.Functions
{
    /// <summary>
    /// Consumes transaction events from transaction-queue.
    /// Writes transaction records to Transactions table and appends to centralized log file.
    /// 
    /// Trigger: Queue message in 'transaction-queue'
    /// Output: Transactions table, Azure Files (applogs/transactions)
    /// 
    /// This is the most straightforward function - proof of concept for the pipeline.
    /// </summary>
    public class TransactionLogFunction
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly ShareServiceClient _shareServiceClient;
        private readonly ILogger _logger;

        public TransactionLogFunction(
            TableServiceClient tableServiceClient,
            ShareServiceClient shareServiceClient,
            ILoggerFactory loggerFactory)
        {
            _tableServiceClient = tableServiceClient;
            _shareServiceClient = shareServiceClient;
            _logger = loggerFactory.CreateLogger<TransactionLogFunction>();
        }

        [Function(nameof(TransactionLogFunction))]
        public async Task Run(
            [QueueTrigger("transaction-queue")] string queueItem,
            FunctionContext context)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                _logger.LogInformation($"TransactionLog function triggered. Message: {queueItem}");

                // Parse the transaction event
                var transactionEvent = JsonSerializer.Deserialize<TransactionEvent>(queueItem)
                    ?? throw new InvalidOperationException("Failed to deserialize transaction event");

                // Create table entity
                var tableClient = _tableServiceClient.GetTableClient("Transactions");
                await tableClient.CreateIfNotExistsAsync();

                var transaction = new TransactionLogEntity
                {
                    PartitionKey = transactionEvent.EventType,
                    RowKey = $"{transactionEvent.OrderId}-{DateTime.UtcNow:yyyyMMddHHmmssffff}",
                    EventType = transactionEvent.EventType,
                    OrderId = transactionEvent.OrderId,
                    Details = JsonSerializer.Serialize(transactionEvent.Details),
                    Status = "Success"
                };

                // Write to table
                await tableClient.AddEntityAsync(transaction);
                _logger.LogInformation($"Transaction logged to table. PartitionKey: {transaction.PartitionKey}, RowKey: {transaction.RowKey}");

                // Append to file — retry loop handles InvalidRange from concurrent invocations
                // writing to the same daily log file simultaneously.
                await AppendToLogAsync(transactionEvent, bytes =>
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] EventType={transactionEvent.EventType} | OrderId={transactionEvent.OrderId} | Status=Success{Environment.NewLine}");

                var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation($"TransactionLog function completed successfully. Execution time: {executionTime:F2}ms");
            }
            catch (JsonException ex)
            {
                _logger.LogError($"JSON deserialization error: {ex.Message}. Message: {queueItem}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in TransactionLog function: {ex.Message}. Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // Appends a log entry to the shared daily file.
        // Retries up to 3 times on InvalidRange (concurrent resize collision).
        // Non-fatal: if all retries fail, logs a warning and continues — the table write already succeeded.
        private async Task AppendToLogAsync(TransactionEvent transactionEvent, Func<byte[], string> entryFactory)
        {
            var fileShare = _shareServiceClient.GetShareClient("applogs");
            await fileShare.CreateIfNotExistsAsync();
            var fileDirectory = fileShare.GetDirectoryClient("transactions");
            await fileDirectory.CreateIfNotExistsAsync();
            var fileName = $"transactions-{DateTime.UtcNow:yyyy-MM-dd}.log";
            var fileClient = fileDirectory.GetFileClient(fileName);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var logEntry = entryFactory(Array.Empty<byte>());
                    var bytes = System.Text.Encoding.UTF8.GetBytes(logEntry);

                    if (await fileClient.ExistsAsync())
                    {
                        // Re-read size on every attempt to get the latest value after a collision
                        var properties = await fileClient.GetPropertiesAsync();
                        long fileSize = properties.Value.ContentLength;
                        await fileClient.SetHttpHeadersAsync(fileSize + bytes.Length);
                        await fileClient.UploadRangeAsync(new Azure.HttpRange(fileSize, bytes.Length), new MemoryStream(bytes));
                    }
                    else
                    {
                        var bytes2 = System.Text.Encoding.UTF8.GetBytes(logEntry);
                        await fileClient.CreateAsync(bytes2.Length);
                        await fileClient.UploadRangeAsync(new Azure.HttpRange(0, bytes2.Length), new MemoryStream(bytes2));
                    }

                    _logger.LogInformation($"Transaction appended to file: {fileName}");
                    return;
                }
                catch (Azure.RequestFailedException ex) when (ex.ErrorCode == "InvalidRange" && attempt < 3)
                {
                    _logger.LogWarning($"File append collision (attempt {attempt}/3), retrying: {ex.ErrorCode}");
                    await Task.Delay(50 * attempt);
                }
                catch (Exception ex)
                {
                    // Non-fatal: table write already succeeded; log and continue
                    _logger.LogWarning($"File append failed (non-fatal, attempt {attempt}/3): {ex.Message}");
                    return;
                }
            }

            _logger.LogWarning($"File append gave up after 3 attempts for {transactionEvent.OrderId} — table record is intact.");
        }
    }
}
