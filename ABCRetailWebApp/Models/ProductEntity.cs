using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailWebApp.Models
{
    public class ProductEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!; // Category
        public string RowKey { get; set; } = default!;      // Product ID
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        [Required(ErrorMessage = "Product Name is required")]
        public string? ProductName { get; set; }

        public string? Description { get; set; }

        // Azure Table Storage does not support decimal natively — store as double.
        [Required(ErrorMessage = "Price is required")]
        public double Price { get; set; }

        [Required(ErrorMessage = "Stock Quantity is required")]
        public int StockQuantity { get; set; }

        [Required(ErrorMessage = "Category is required")]
        public string? Category { get; set; }

        // Clothing specific
        public string? Size { get; set; }      // S, M, L, XL
        public string? Color { get; set; }
        public string? Material { get; set; }
        public string? Brand { get; set; }

        public string? ImageBlobName { get; set; }
        public string? ImageUrl { get; set; }  // Fallback URL (e.g. Unsplash) when no blob uploaded
        public DateTime CreatedAt { get; set; }
        public bool IsAvailable { get; set; }

        public ProductEntity()
        {
            CreatedAt = DateTime.UtcNow;
            IsAvailable = true;
        }
    }
}