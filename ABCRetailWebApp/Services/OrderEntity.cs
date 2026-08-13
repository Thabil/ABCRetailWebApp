using System.Text.Json;

namespace ABCRetailWebApp.Services
{
    public class OrderEntity
    {
        public string? OrderId { get; set; }
        public string? CustomerId { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static OrderEntity FromJson(string json)
        {
            return JsonSerializer.Deserialize<OrderEntity>(json) ?? new OrderEntity();
        }

        public OrderEntity()
        {
            OrderId = Guid.NewGuid().ToString();
            CreatedAt = DateTime.UtcNow;
            Status = "Pending";
        }
    }
}