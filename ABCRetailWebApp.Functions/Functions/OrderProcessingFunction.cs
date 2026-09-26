using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using System.Text.Json;
using ABCRetailWebApp.Functions.Models;

namespace ABCRetailWebApp.Functions.Functions
{
    /// <summary>
    /// Consumes order placement events from order-processing-queue.
    /// 
    /// Operations:
    /// 1. Writes audit record to OrderAudit table
    /// 2. Publishes transaction event to transaction-queue for centralized logging
    /// 
    /// Trigger: Queue message in 'order-processing-queue'
    /// Output: OrderAudit table, transaction-queue
    /// 
    /// This function validates and audits orders, creating a permanent audit trail.
    /// </summary>
    public class OrderProcessingFunction
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger _logger;

        public OrderProcessingFunction(
            TableServiceClient tableServiceClient,
            QueueServiceClient queueServiceClient,
            ILoggerFactory loggerFactory)
        {
            _tableServiceClient = tableServiceClient;
            _queueServiceClient = queueServiceClient;
            _logger = loggerFactory.CreateLogger<OrderProcessingFunction>();
        }

        [Function(nameof(OrderProcessingFunction))]
        public async Task Run(
            [QueueTrigger("order-processing-queue")] string queueItem,
            FunctionContext context)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                _logger.LogInformation($"OrderProcessing function triggered. Message: {queueItem}");

                // Parse the order event
                var orderEvent = JsonSerializer.Deserialize<OrderEvent>(queueItem)
                    ?? throw new InvalidOperationException("Failed to deserialize order event");

                // Step 1: Write to OrderAudit table
                var tableClient = _tableServiceClient.GetTableClient("OrderAudit");
                await tableClient.CreateIfNotExistsAsync();

                var orderAudit = new OrderAuditEntity
                {
                    PartitionKey = orderEvent.CustomerId,
                    RowKey = $"{orderEvent.OrderId}-{DateTime.UtcNow:yyyyMMddHHmmssffff}",
                    OrderId = orderEvent.OrderId,
                    CustomerId = orderEvent.CustomerId,
                    CustomerName = orderEvent.CustomerName,
                    CustomerEmail = orderEvent.CustomerEmail,
                    Total = orderEvent.Total,
                    Status = "Processed",
                    FunctionVersion = "1.0",
                    Details = JsonSerializer.Serialize(new
                    {
                        ProcessedAt = DateTime.UtcNow,
                        Source = "OrderProcessingFunction"
                    })
                };

                await tableClient.AddEntityAsync(orderAudit);
                _logger.LogInformation($"Order audit saved. OrderId: {orderEvent.OrderId}, CustomerId: {orderEvent.CustomerId}, Total: {orderEvent.Total}");

                // Step 2: Publish transaction event for centralized logging
                await PublishTransactionEvent(orderEvent, startTime);

                _logger.LogInformation($"OrderProcessing function completed successfully");
            }
            catch (JsonException ex)
            {
                _logger.LogError($"JSON deserialization error: {ex.Message}. Message: {queueItem}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in OrderProcessing function: {ex.Message}. Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task PublishTransactionEvent(OrderEvent orderEvent, DateTime startTime)
        {
            try
            {
                var transactionEvent = new TransactionEvent
                {
                    EventType = "OrderProcessed",
                    OrderId = orderEvent.OrderId,
                    Timestamp = DateTime.UtcNow,
                    Details = new Dictionary<string, object>
                    {
                        { "CustomerId", orderEvent.CustomerId },
                        { "CustomerName", orderEvent.CustomerName },
                        { "CustomerEmail", orderEvent.CustomerEmail },
                        { "Total", orderEvent.Total }
                    }
                };

                var queueClient = _queueServiceClient.GetQueueClient("transaction-queue");
                await queueClient.CreateIfNotExistsAsync();

                var messageJson = JsonSerializer.Serialize(transactionEvent);
                await queueClient.SendMessageAsync(messageJson);

                _logger.LogInformation($"Transaction event published to queue for order: {orderEvent.OrderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error publishing transaction event: {ex.Message}");
                throw;
            }
        }
    }
}
