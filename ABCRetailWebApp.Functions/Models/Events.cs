using System.Text.Json.Serialization;

namespace ABCRetailWebApp.Functions.Models
{
    /// <summary>
    /// Base class for all events that flow through Azure Functions.
    /// Functions deserialize queue messages into these types.
    /// </summary>
    public class EventBase
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Order placement event from web app checkout.
    /// Triggers OrderProcessing function.
    /// </summary>
    public class OrderEvent : EventBase
    {
        [JsonPropertyName("orderId")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("customerId")]
        public string CustomerId { get; set; } = string.Empty;

        [JsonPropertyName("customerName")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("customerEmail")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("total")]
        public decimal Total { get; set; }
    }

    /// <summary>
    /// Stock alert event when inventory depletes.
    /// Triggers StockAlerts function.
    /// </summary>
    public class StockAlertEvent : EventBase
    {
        [JsonPropertyName("productId")]
        public string ProductId { get; set; } = string.Empty;

        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("remainingStock")]
        public int RemainingStock { get; set; }
    }

    /// <summary>
    /// Transaction event for inter-function communication.
    /// Published by OrderProcessing and StockAlerts functions.
    /// Triggers TransactionLog function.
    /// </summary>
    public class TransactionEvent : EventBase
    {
        [JsonPropertyName("orderId")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public Dictionary<string, object> Details { get; set; } = new();
    }
}
