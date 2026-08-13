namespace ABCRetailWebApp.Models
{
    public class CartItem
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public double Price { get; set; }
        public int Quantity { get; set; }
        public string? ImageBlobName { get; set; }
        public double Total => Price * Quantity;
    }
}