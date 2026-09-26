using Azure;
using Azure.Data.Tables;

namespace ABCRetailWebApp.Functions.Models
{
    /// <summary>
    /// Audit trail for processed orders.
    /// Tracks when orders are processed by Azure Functions.
    /// </summary>
    public class OrderAuditEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string OrderId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public string Status { get; set; } = "Processed";
        public string FunctionVersion { get; set; } = "1.0";
        public string Details { get; set; } = string.Empty;
    }

    /// <summary>
    /// History of stock alert events.
    /// Tracks inventory depletion alerts for reporting.
    /// </summary>
    public class StockAlertEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int RemainingStock { get; set; }
        public int AlertCount { get; set; } = 1;
        public DateTime LastAlertTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Centralized transaction log.
    /// All events from all functions are logged here for audit and analytics.
    /// </summary>
    public class TransactionLogEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string EventType { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string? CustomerId { get; set; }
        public string? ProductId { get; set; }
        public string Source { get; set; } = "AzureFunctions";
        public string Status { get; set; } = "Success";
        public string Details { get; set; } = string.Empty;
        public long ExecutionTimeMs { get; set; }
    }

    /// <summary>
    /// Report metadata and history.
    /// Tracks generated reports for traceability.
    /// </summary>
    public class ReportEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string ReportType { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public string BlobPath { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
}
