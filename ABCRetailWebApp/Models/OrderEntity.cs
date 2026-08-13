using Azure;
using Azure.Data.Tables;

namespace ABCRetailWebApp.Models
{
    public class OrderEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!; // CustomerId (email)
        public string RowKey { get; set; } = default!;      // OrderId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string? OrderId { get; set; }
        public string? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? ShippingAddress { get; set; }

        // Order items as JSON
        public string? OrderItemsJson { get; set; }

        // Azure Table Storage does not support decimal natively — store as double.
        public double SubTotal { get; set; }
        public double Tax { get; set; }
        public double ShippingCost { get; set; }
        public double TotalAmount { get; set; }

        public string? PaymentMethod { get; set; } // Credit Card, EFT
        public string? PaymentStatus { get; set; } // Paid, Pending, Failed

        // Order status
        public string? OrderStatus { get; set; }   // Processing, Shipped, Delivered, Cancelled
        public string? TrackingNumber { get; set; }
        public string? Courier { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public bool IsCompleted { get; set; }

        public OrderEntity()
        {
            CreatedAt = DateTime.UtcNow;
            OrderStatus = "Processing";
            PaymentStatus = "Pending";
            IsCompleted = false;
        }
    }
}