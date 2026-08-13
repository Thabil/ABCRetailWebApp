using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ABCRetailWebApp.Models;

namespace ABCRetailWebApp.Services
{
    public class AuthService
    {
        private readonly ITableStorageService<CustomerEntity> _customerService;

        public AuthService(ITableStorageService<CustomerEntity> customerService)
        {
            _customerService = customerService;
        }

        // ============================================================
        // REGISTER NEW USER
        // ============================================================

        public async Task<(bool Success, string Message)> RegisterAsync(
            string firstName,
            string lastName,
            string email,
            string password,
            string userType = "Customer")
        {
            try
            {
                var lowerEmail = email.ToLower();

                // Check if user already exists (search both partitions)
                var existing = await _customerService.GetEntityAsync(userType, lowerEmail);
                if (existing == null && userType == "Customer")
                {
                    existing = await _customerService.GetEntityAsync("Admin", lowerEmail);
                }

                if (existing != null)
                {
                    return (false, "User already exists");
                }

                // Create new user
                var user = new CustomerEntity(lowerEmail, userType)
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    PasswordHash = HashPassword(password),
                    IsActive = true,
                    Phone = "",
                    DeliveryAddress = "",
                    City = "",
                    Province = "",
                    PostalCode = ""
                };

                await _customerService.AddEntityAsync(user);
                return (true, "Registration successful");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        // ============================================================
        // LOGIN USER
        // ============================================================

        public async Task<(bool Success, CustomerEntity? User, string Message)> LoginAsync(
            string email,
            string password)
        {
            try
            {
                var lowerEmail = email.ToLower();

                // Try Customer partition first
                var user = await _customerService.GetEntityAsync("Customer", lowerEmail);

                // If not found, try Admin partition
                if (user == null)
                {
                    user = await _customerService.GetEntityAsync("Admin", lowerEmail);
                }

                if (user == null)
                {
                    return (false, null, "User not found");
                }

                // Verify password
                if (!VerifyPassword(password, user.PasswordHash))
                {
                    return (false, null, "Invalid password");
                }

                // Update last login
                user.LastLoginAt = DateTime.UtcNow;
                await _customerService.UpdateEntityAsync(user);

                return (true, user, "Login successful");
            }
            catch (Exception ex)
            {
                return (false, null, $"Error: {ex.Message}");
            }
        }

        // ============================================================
        // GET USER BY EMAIL
        // ============================================================

        public async Task<CustomerEntity?> GetUserByEmailAsync(string email)
        {
            var lowerEmail = email.ToLower();

            // Check Customer partition first
            var user = await _customerService.GetEntityAsync("Customer", lowerEmail);
            if (user != null) return user;

            // Check Admin partition
            return await _customerService.GetEntityAsync("Admin", lowerEmail);
        }

        // ============================================================
        // PASSWORD HASHING (SHA256 - Simple)
        // ============================================================

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        private bool VerifyPassword(string password, string? hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            var hashedPassword = HashPassword(password);
            return hashedPassword == hash;
        }

        // ============================================================
        // SESSION MANAGEMENT
        // ============================================================

        public void SetUserSession(HttpContext context, CustomerEntity user)
        {
            var sessionData = JsonSerializer.Serialize(new
            {
                user.Email,
                user.FirstName,
                user.LastName,
                user.UserType,
                user.RowKey,
                user.PartitionKey
            });

            context.Session.SetString("User", sessionData);
            context.Session.SetString("IsLoggedIn", "true");
        }

        public CustomerEntity? GetUserFromSession(HttpContext context)
        {
            var sessionData = context.Session.GetString("User");
            if (string.IsNullOrEmpty(sessionData)) return null;

            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(sessionData);
                if (data == null) return null;

                return new CustomerEntity
                {
                    Email = data["Email"],
                    FirstName = data["FirstName"],
                    LastName = data["LastName"],
                    UserType = data["UserType"],
                    RowKey = data["RowKey"],
                    PartitionKey = data["PartitionKey"]
                };
            }
            catch
            {
                return null;
            }
        }

        public bool IsLoggedIn(HttpContext context)
        {
            return context.Session.GetString("IsLoggedIn") == "true";
        }

        public bool IsAdmin(HttpContext context)
        {
            var user = GetUserFromSession(context);
            return user != null && user.UserType == "Admin";
        }

        public void Logout(HttpContext context)
        {
            context.Session.Clear();
        }
    }
}