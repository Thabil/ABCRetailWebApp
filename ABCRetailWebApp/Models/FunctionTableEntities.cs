using Azure;
using Azure.Data.Tables;

namespace ABCRetailWebApp.Models
{
    // Read-only mirror of the OrderAudit table written by OrderProcessingFunction
    public class OrderAuditEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // CustomerId
        public string RowKey       { get; set; } = string.Empty; // OrderId-timestamp
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string OrderId      { get; set; } = string.Empty;
        public string CustomerId   { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public decimal Total       { get; set; }
        public string Status       { get; set; } = string.Empty;
        public string Details      { get; set; } = string.Empty;
    }

    // Read-only mirror of the StockAlerts table written by StockAlertsFunction
    public class StockAlertEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // ProductId
        public string RowKey       { get; set; } = string.Empty; // ProductId (stable upsert key)
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string ProductId    { get; set; } = string.Empty;
        public string ProductName  { get; set; } = string.Empty;
        public string Category     { get; set; } = string.Empty;
        public int    RemainingStock { get; set; }
        public DateTime LastAlertTime { get; set; }
    }

    // Read-only mirror of the Transactions table written by TransactionLogFunction
    public class TransactionLogEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // EventType
        public string RowKey       { get; set; } = string.Empty; // OrderId/ProductId-timestamp
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string EventType { get; set; } = string.Empty;
        public string OrderId   { get; set; } = string.Empty;
        public string Status    { get; set; } = string.Empty;
        public string Details   { get; set; } = string.Empty;
    }
}
