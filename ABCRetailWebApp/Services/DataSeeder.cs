using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ABCRetailWebApp.Models;
using Azure.Data.Tables;
using Azure.Storage.Queues;

namespace ABCRetailWebApp.Services
{
    public class DataSeeder
    {
        private readonly TableStorageService<CustomerEntity>             _customers;
        private readonly TableStorageService<ProductEntity>              _products;
        private readonly TableStorageService<ABCRetailWebApp.Models.OrderEntity> _orders;
        private readonly QueueClient _queueClient;

        public DataSeeder(TableServiceClient tableServiceClient, QueueServiceClient queueServiceClient)
        {
            _customers   = new TableStorageService<CustomerEntity>(tableServiceClient, "Customers");
            _products    = new TableStorageService<ProductEntity>(tableServiceClient,  "Products");
            _orders      = new TableStorageService<ABCRetailWebApp.Models.OrderEntity>(tableServiceClient, "Orders");
            _queueClient = queueServiceClient.GetQueueClient("order-processing-queue");
            _queueClient.CreateIfNotExists();
        }

        public async Task<SeedResult> RunAsync(string seedFilePath, bool overwrite = false)
        {
            var result = new SeedResult();

            if (!File.Exists(seedFilePath))
            {
                result.Errors.Add($"Seed file not found: {seedFilePath}");
                return result;
            }

            var json = await File.ReadAllTextAsync(seedFilePath);
            var doc  = JsonDocument.Parse(json);

            await SeedCustomersAsync(doc.RootElement.GetProperty("customers"), result, overwrite);
            await SeedProductsAsync(doc.RootElement.GetProperty("products"),   result, overwrite);

            await SeedOrdersAsync(doc.RootElement.GetProperty("orders"),       result, overwrite);

            return result;
        }

        // ============================================================
        // CUSTOMERS
        // ============================================================

        private async Task SeedCustomersAsync(JsonElement items, SeedResult result, bool overwrite)
        {
            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    var partitionKey = item.GetProperty("partitionKey").GetString()!;
                    var rowKey       = item.GetProperty("rowKey").GetString()!;

                    var exists = await _customers.EntityExistsAsync(partitionKey, rowKey);
                    if (exists && !overwrite)
                    {
                        result.Skipped++;
                        continue;
                    }

                    var entity = new CustomerEntity
                    {
                        PartitionKey    = partitionKey,
                        RowKey          = rowKey,
                        FirstName       = item.GetProperty("firstName").GetString(),
                        LastName        = item.GetProperty("lastName").GetString(),
                        Email           = item.GetProperty("email").GetString(),
                        PasswordHash    = HashPassword(item.GetProperty("password").GetString()!),
                        Phone           = item.GetProperty("phone").GetString(),
                        City            = item.GetProperty("city").GetString(),
                        DeliveryAddress = item.GetProperty("deliveryAddress").GetString(),
                        Province        = item.GetProperty("province").GetString(),
                        PostalCode      = item.GetProperty("postalCode").GetString(),
                        UserType        = item.GetProperty("userType").GetString(),
                        IsActive        = true,
                        CreatedAt       = DateTime.UtcNow
                    };

                    if (exists)
                        await _customers.UpdateEntityAsync(entity);
                    else
                        await _customers.AddEntityAsync(entity);

                    result.Inserted++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Customer {item.GetProperty("rowKey").GetString()}: {ex.Message}");
                }
            }
        }

        // ============================================================
        // PRODUCTS
        // ============================================================

        private async Task SeedProductsAsync(JsonElement items, SeedResult result, bool overwrite)
        {
            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    var partitionKey = item.GetProperty("partitionKey").GetString()!;
                    var rowKey       = item.GetProperty("rowKey").GetString()!;

                    var exists = await _products.EntityExistsAsync(partitionKey, rowKey);
                    if (exists && !overwrite)
                    {
                        result.Skipped++;
                        continue;
                    }

                    var entity = new ProductEntity
                    {
                        PartitionKey  = partitionKey,
                        RowKey        = rowKey,
                        ProductName   = item.GetProperty("productName").GetString(),
                        Description   = GetOptionalString(item, "description"),
                        Price         = item.GetProperty("price").GetDouble(),
                        StockQuantity = item.GetProperty("stockQuantity").GetInt32(),
                        Category      = item.GetProperty("category").GetString(),
                        Size          = GetOptionalString(item, "size"),
                        Color         = GetOptionalString(item, "color"),
                        Material      = GetOptionalString(item, "material"),
                        Brand         = GetOptionalString(item, "brand"),
                        ImageUrl      = GetOptionalString(item, "imageUrl"),
                        IsAvailable   = true,
                        CreatedAt     = DateTime.UtcNow
                    };

                    if (exists)
                        await _products.UpdateEntityAsync(entity);
                    else
                        await _products.AddEntityAsync(entity);

                    result.Inserted++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Product {item.GetProperty("rowKey").GetString()}: {ex.Message}");
                }
            }
        }

        // ============================================================
        // ORDERS
        // ============================================================

        private async Task SeedOrdersAsync(JsonElement items, SeedResult result, bool overwrite)
        {
            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    var partitionKey = item.GetProperty("partitionKey").GetString()!;
                    var rowKey       = item.GetProperty("rowKey").GetString()!;

                    var exists = await _orders.EntityExistsAsync(partitionKey, rowKey);
                    if (exists && !overwrite)
                    {
                        result.Skipped++;
                        continue;
                    }

                    var entity = new ABCRetailWebApp.Models.OrderEntity
                    {
                        PartitionKey    = partitionKey,
                        RowKey          = rowKey,
                        OrderId         = item.GetProperty("orderId").GetString(),
                        CustomerId      = item.GetProperty("customerId").GetString(),
                        CustomerName    = item.GetProperty("customerName").GetString(),
                        CustomerEmail   = item.GetProperty("customerEmail").GetString(),
                        ShippingAddress = item.GetProperty("shippingAddress").GetString(),
                        OrderItemsJson  = item.GetProperty("orderItemsJson").GetString(),
                        SubTotal        = item.GetProperty("subTotal").GetDouble(),
                        ShippingCost    = item.GetProperty("shippingCost").GetDouble(),
                        TotalAmount     = item.GetProperty("totalAmount").GetDouble(),
                        PaymentMethod   = item.GetProperty("paymentMethod").GetString(),
                        PaymentStatus   = item.GetProperty("paymentStatus").GetString(),
                        OrderStatus     = item.GetProperty("orderStatus").GetString(),
                        TrackingNumber  = GetOptionalString(item, "trackingNumber"),
                        Courier         = GetOptionalString(item, "courier"),
                        IsCompleted     = item.GetProperty("isCompleted").GetBoolean(),
                        CreatedAt       = item.GetProperty("createdAt").GetDateTime(),
                        ShippedAt       = GetOptionalDateTime(item, "shippedAt"),
                        DeliveredAt     = GetOptionalDateTime(item, "deliveredAt"),
                        CancelledAt     = GetOptionalDateTime(item, "cancelledAt")
                    };

                    if (exists)
                        await _orders.UpdateEntityAsync(entity);
                    else
                        await _orders.AddEntityAsync(entity);

                    // Only push to queue if this is a freshly-created order (not historical seed data)
                    // Use a 1-hour window (not 24h) to avoid re-queuing orders placed earlier the same day on restart
                    var isRecent = (DateTime.UtcNow - entity.CreatedAt).TotalMinutes < 60;
                    if (!exists && isRecent && !entity.IsCompleted && (entity.OrderStatus == "Processing" || entity.OrderStatus == "Pending"))
                    {
                        var msg = EventMessageHelper.CreateOrderEvent(
                            entity.OrderId!,
                            entity.CustomerId!,
                            entity.CustomerName!,
                            entity.CustomerEmail!,
                            (decimal)entity.TotalAmount,
                            entity.CreatedAt
                        );
                        await _queueClient.SendMessageAsync(msg);
                    }

                    result.Inserted++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Order {item.GetProperty("rowKey").GetString()}: {ex.Message}");
                }
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            return Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(password)));
        }

        private static string? GetOptionalString(JsonElement el, string key) =>
            el.TryGetProperty(key, out var prop) ? prop.GetString() : null;

        private static DateTime? GetOptionalDateTime(JsonElement el, string key) =>
            el.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Null
                ? prop.GetDateTime()
                : null;
    }

    public class SeedResult
    {
        public int Inserted { get; set; }
        public int Skipped  { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool HasErrors => Errors.Count > 0;
    }
}
