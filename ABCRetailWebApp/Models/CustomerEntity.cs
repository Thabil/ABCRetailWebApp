using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailWebApp.Models
{
    public class CustomerEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!; // "Customer"
        public string RowKey { get; set; } = default!;      // Email (lowercase)
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        [Required(ErrorMessage = "First Name is required")]
        public string? FirstName { get; set; }

        [Required(ErrorMessage = "Last Name is required")]
        public string? LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string? PasswordHash { get; set; }

        [Required(ErrorMessage = "Phone Number is required")]
        [Phone]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "City is required")]
        public string? City { get; set; }

        public string? DeliveryAddress { get; set; }
        public string? Province { get; set; }
        public string? PostalCode { get; set; }
        public string? UserType { get; set; } // "Customer" or "Admin"
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        public CustomerEntity()
        {
            CreatedAt = DateTime.UtcNow;
            IsActive = true;
            UserType = "Customer";
        }

        // Compatibility constructor used by registration and other helpers
        public CustomerEntity(string rowKey, string partitionKey) : this()
        {
            RowKey = rowKey;
            PartitionKey = partitionKey;
        }

        // Backwards-compatible alias: some views expect Address property name
        public string? Address
        {
            get => DeliveryAddress;
            set => DeliveryAddress = value;
        }
    }
}