using System.Text.Json;

namespace ABCRetailWebApp.Services
{
    /// <summary>
    /// Converts application events to JSON format for Azure Functions to consume.
    /// Minimal code changes: Web app calls these helpers instead of string concatenation.
    /// </summary>
    public static class EventMessageHelper
    {
        /// <summary>
        /// Creates a JSON message for order placement events.
        /// Replaces: $"Order {orderId} placed by {customerName} | Total: R{total:F2}"
        /// </summary>
        public static string CreateOrderEvent(
            string orderId,
            string customerId,
            string customerName,
            string customerEmail,
            decimal total,
            DateTime timestamp)
        {
            var @event = new
            {
                eventType = "OrderPlaced",
                orderId = orderId,
                customerId = customerId,
                customerName = customerName,
                customerEmail = customerEmail,
                total = total,
                timestamp = timestamp.ToUniversalTime().ToString("O")
            };

            return JsonSerializer.Serialize(@event);
        }

        /// <summary>
        /// Creates a JSON message for stock alert events.
        /// Replaces: $"OUT_OF_STOCK: {productName} | ID: {productId} | Category: {category}"
        /// </summary>
        public static string CreateStockAlertEvent(
            string productId,
            string productName,
            string category,
            int remainingStock,
            DateTime timestamp)
        {
            var @event = new
            {
                eventType = "OutOfStock",
                productId = productId,
                productName = productName,
                category = category,
                remainingStock = remainingStock,
                timestamp = timestamp.ToUniversalTime().ToString("O")
            };

            return JsonSerializer.Serialize(@event);
        }

        /// <summary>
        /// Creates a JSON message for transaction events (for internal function-to-function communication).
        /// </summary>
        public static string CreateTransactionEvent(
            string eventType,
            string orderId,
            Dictionary<string, object> details,
            DateTime timestamp)
        {
            var @event = new
            {
                eventType = eventType,
                orderId = orderId,
                details = details,
                timestamp = timestamp.ToUniversalTime().ToString("O")
            };

            return JsonSerializer.Serialize(@event);
        }
    }
}
