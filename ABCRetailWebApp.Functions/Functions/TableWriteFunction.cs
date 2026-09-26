using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Azure.Storage.Files.Shares;
using System.Net;
using System.Text.Json;

namespace ABCRetailWebApp.Functions.Functions
{
    /// <summary>
    /// HTTP-triggered function that writes a product entity to Azure Table Storage.
    ///
    /// The web app calls this after its own direct table write (optimistic local write pattern).
    /// This function handles: audit trail (file log) + queue notification (product-events).
    /// Uses UpsertEntity so a duplicate write from the web app is always safe.
    ///
    /// POST /api/TableWriteFunction
    /// Body: ProductWriteRequest JSON
    /// Returns: 200 { "success": true, "rowKey": "..." } or 400/500 with error detail
    /// </summary>
    public class TableWriteFunction
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ShareServiceClient _shareServiceClient;
        private readonly ILogger _logger;

        public TableWriteFunction(
            TableServiceClient tableServiceClient,
            QueueServiceClient queueServiceClient,
            ShareServiceClient shareServiceClient,
            ILoggerFactory loggerFactory)
        {
            _tableServiceClient = tableServiceClient;
            _queueServiceClient = queueServiceClient;
            _shareServiceClient = shareServiceClient;
            _logger = loggerFactory.CreateLogger<TableWriteFunction>();
        }

        [Function(nameof(TableWriteFunction))]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("TableWriteFunction triggered");

            ProductWriteRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<ProductWriteRequest>(
                    req.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.RowKey) || string.IsNullOrEmpty(request.PartitionKey))
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteStringAsync("RowKey and PartitionKey are required");
                    return bad;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError("Failed to deserialize request: {Message}", ex.Message);
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync($"Invalid JSON: {ex.Message}");
                return bad;
            }

            try
            {
                // Upsert to Products table — safe even if web app already wrote it
                var tableClient = _tableServiceClient.GetTableClient("Products");
                await tableClient.CreateIfNotExistsAsync();

                var entity = new TableEntity(request.PartitionKey, request.RowKey)
                {
                    ["ProductName"]   = request.ProductName,
                    ["Description"]   = request.Description,
                    ["Price"]         = request.Price,
                    ["StockQuantity"] = request.StockQuantity,
                    ["Category"]      = request.Category,
                    ["Brand"]         = request.Brand,
                    ["Size"]          = request.Size,
                    ["Color"]         = request.Color,
                    ["Material"]      = request.Material,
                    ["ImageBlobName"] = request.ImageBlobName,
                    ["IsAvailable"]   = request.IsAvailable,
                    ["CreatedAt"]     = request.CreatedAt
                };

                await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge);
                _logger.LogInformation("Product upserted. RowKey: {RowKey}, Name: {Name}", request.RowKey, request.ProductName);

                // Publish to product-events queue
                var queueClient = _queueServiceClient.GetQueueClient("product-events");
                await queueClient.CreateIfNotExistsAsync();
                var queueMessage = JsonSerializer.Serialize(new
                {
                    eventType  = "ProductCreated",
                    productId  = request.RowKey,
                    productName = request.ProductName,
                    category   = request.Category,
                    timestamp  = DateTime.UtcNow.ToString("O")
                });
                await queueClient.SendMessageAsync(queueMessage);
                _logger.LogInformation("Published to product-events queue");

                // Append to file log
                await AppendToLogAsync($"product-events-{DateTime.UtcNow:yyyy-MM-dd}.log",
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PRODUCT_CREATED | ID: {request.RowKey} | Name: {request.ProductName} | Category: {request.Category}");

                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { success = true, rowKey = request.RowKey });
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in TableWriteFunction: {Message}", ex.Message);
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteStringAsync($"Function error: {ex.Message}");
                return error;
            }
        }

        private async Task AppendToLogAsync(string fileName, string entry)
        {
            try
            {
                var share = _shareServiceClient.GetShareClient("applogs");
                await share.CreateIfNotExistsAsync();
                var dir = share.GetDirectoryClient("logs");
                await dir.CreateIfNotExistsAsync();
                var fileClient = dir.GetFileClient(fileName);
                var bytes = System.Text.Encoding.UTF8.GetBytes(entry + Environment.NewLine);

                if (await fileClient.ExistsAsync())
                {
                    var props = await fileClient.GetPropertiesAsync();
                    long existing = props.Value.ContentLength;
                    await fileClient.SetHttpHeadersAsync(existing + bytes.Length);
                    await fileClient.UploadRangeAsync(new Azure.HttpRange(existing, bytes.Length), new MemoryStream(bytes));
                }
                else
                {
                    await fileClient.CreateAsync(bytes.Length);
                    await fileClient.UploadRangeAsync(new Azure.HttpRange(0, bytes.Length), new MemoryStream(bytes));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Log append failed (non-fatal): {Message}", ex.Message);
            }
        }
    }

    public class ProductWriteRequest
    {
        public string PartitionKey  { get; set; } = string.Empty;
        public string RowKey        { get; set; } = string.Empty;
        public string? ProductName  { get; set; }
        public string? Description  { get; set; }
        public double Price         { get; set; }
        public int StockQuantity    { get; set; }
        public string? Category     { get; set; }
        public string? Brand        { get; set; }
        public string? Size         { get; set; }
        public string? Color        { get; set; }
        public string? Material     { get; set; }
        public string? ImageBlobName { get; set; }
        public bool IsAvailable     { get; set; }
        public DateTime CreatedAt   { get; set; }
    }
}
