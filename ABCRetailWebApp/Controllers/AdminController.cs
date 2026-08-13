using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using ABCRetailWebApp.Services;
using System.Text.Json;

namespace ABCRetailWebApp.Controllers
{
    public class AdminController : Controller
    {
        private readonly AuthService _authService;
        private readonly ITableStorageService<CustomerEntity> _customerService;
        private readonly ITableStorageService<ProductEntity> _productService;
        private readonly ITableStorageService<ABCRetailWebApp.Models.OrderEntity> _orderService;
        private readonly IBlobStorageService _blobService;
        private readonly IQueueStorageService _queueService;
        private readonly IFileStorageService _fileService;

        public AdminController(
            AuthService authService,
            ITableStorageService<CustomerEntity> customerService,
            ITableStorageService<ProductEntity> productService,
            ITableStorageService<ABCRetailWebApp.Models.OrderEntity> orderService,
            IBlobStorageService blobService,
            IQueueStorageService queueService,
            IFileStorageService fileService)
        {
            _authService = authService;
            _customerService = customerService;
            _productService = productService;
            _orderService = orderService;
            _blobService = blobService;
            _queueService = queueService;
            _fileService = fileService;
        }

        // ============================================================
        // ADMIN DASHBOARD
        // ============================================================

        public async Task<IActionResult> Dashboard()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var customers = await _customerService.GetEntitiesAsync();
            var products = await _productService.GetEntitiesAsync();
            var orders = await _orderService.GetEntitiesAsync();

            var viewModel = new ABCRetailWebApp.Models.AdminDashboardViewModel
            {
                TotalCustomers = customers.Count(),
                InventoryItems = products.Count(),
                TotalOrders = orders.Count(),
                PendingOrders = orders.Count(o => o.OrderStatus == "Processing" || o.OrderStatus == "Pending"),
                AuditLogEntries = await GetLogCountAsync(),
                TotalRevenue = orders.Sum(o => o.TotalAmount),
                AllOrders = orders.OrderByDescending(o => o.CreatedAt).ToList()
            };

            // Get infrastructure overview
            ViewBag.Infrastructure = GetInfrastructureOverview();

            return View(viewModel);
        }

        // ============================================================
        // ADMIN - ORDERS (Consolidated View)
        // ============================================================

        public async Task<IActionResult> Orders()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var orders = await _orderService.GetEntitiesAsync();
            return View(orders.OrderByDescending(o => o.CreatedAt));
        }

        // ============================================================
        // ADMIN - INVENTORY
        // ============================================================

        public async Task<IActionResult> Inventory()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var products = await _productService.GetEntitiesAsync();
            return View(products);
        }

        // ============================================================
        // ADMIN - CUSTOMERS
        // ============================================================

        public async Task<IActionResult> Customers()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var customers = await _customerService.GetEntitiesAsync();
            return View(customers);
        }

        // ============================================================
        // ADMIN - ORDER QUEUE (Processing)
        // ============================================================

        public async Task<IActionResult> Queue()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var messages = await _queueService.PeekMessagesAsync("order-processing-queue", 20);
            var queueMessages = new List<QueueMessageModel>();

            foreach (var msg in messages)
            {
                queueMessages.Add(new QueueMessageModel
                {
                    Content = msg,
                    Timestamp = DateTime.UtcNow
                });
            }

            return View(queueMessages);
        }

        // ============================================================
        // ADMIN - PROCESS NEXT QUEUE MESSAGE
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> ProcessNextMessage()
        {
            if (!_authService.IsAdmin(HttpContext))
                return Json(new { success = false, message = "Unauthorised" });

            var result = await _queueService.ReceiveMessageAsync("order-processing-queue");

            if (result == null)
                return Json(new { success = false, message = "Queue is empty — no messages to process." });

            var messageText = result.Value.Text;

            // Parse OrderId from message format: "Order ORD-xxx by Name | R... | Status: ..."
            string? orderId = null;
            var match = System.Text.RegularExpressions.Regex.Match(messageText, @"Order (ORD-[\w-]+)");
            if (match.Success)
            {
                orderId = match.Groups[1].Value;
                var allOrders = await _orderService.GetEntitiesAsync();
                var order = allOrders.FirstOrDefault(o => o.OrderId == orderId);

                if (order != null)
                {
                    order.OrderStatus = "Shipped";
                    order.ShippedAt   = DateTime.UtcNow;
                    await _orderService.UpdateEntityAsync(order);
                }
            }

            await _fileService.AppendToLogAsync($"queue-{DateTime.Now:yyyy-MM-dd}.log",
                $"[{DateTime.UtcNow}] Processed: {messageText}");

            return Json(new { success = true, message = $"Processed: {messageText} → Shipped" });
        }

        // ============================================================
        // ADMIN - PURGE QUEUE
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> PurgeQueue()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            await _queueService.ClearQueueAsync("order-processing-queue");

            await _fileService.AppendToLogAsync($"queue-{DateTime.Now:yyyy-MM-dd}.log",
                $"[{DateTime.UtcNow}] Queue purged by admin");

            return RedirectToAction("Queue");
        }

        // ============================================================
        // ADMIN - SYSTEM LOGS
        // ============================================================

        public async Task<IActionResult> SystemLogs(string? logType)
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var logFiles = new List<(string Name, string Content)>();
            var files = await _fileService.ListLogFilesAsync();

            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(logType) && !file.Contains(logType))
                    continue;

                var content = await _fileService.DownloadLogFileAsync(file);
                logFiles.Add((file, content));
            }

            return View(logFiles);
        }

        // ============================================================
        // ADMIN - INFRASTRUCTURE OVERVIEW
        // ============================================================

        public IActionResult Infrastructure()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var overview = GetInfrastructureOverview();
            return View(overview);
        }

        // ============================================================
        // ADMIN - ADD NEW PRODUCT (Quick Action)
        // ============================================================

        [HttpGet]
        public IActionResult AddProduct()
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AddProduct(ProductEntity product, IFormFile? imageFile)
        {
            if (!_authService.IsAdmin(HttpContext))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            try
            {
                product.RowKey = Guid.NewGuid().ToString();
                product.PartitionKey = product.Category ?? "General";

                if (imageFile != null && imageFile.Length > 0)
                {
                    product.ImageBlobName = await _blobService.UploadImageAsync(
                        imageFile, $"product_{product.RowKey}");
                }

                product.CreatedAt = DateTime.UtcNow;
                product.IsAvailable = true;

                await _productService.AddEntityAsync(product);

                TempData["Success"] = $"Product '{product.ProductName}' added successfully!";
                return RedirectToAction("Inventory");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error: {ex.Message}");
                return View(product);
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteProduct(string partitionKey, string rowKey)
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            await _productService.DeleteEntityAsync(partitionKey, rowKey);
            TempData["Success"] = "Product deleted successfully.";
            return RedirectToAction("Inventory");
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<int> GetLogCountAsync()
        {
            try
            {
                var files = await _fileService.ListLogFilesAsync();
                var count = 0;
                foreach (var file in files)
                {
                    var content = await _fileService.DownloadLogFileAsync(file);
                    count += content.Split('\n').Length;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private List<InfrastructureItem> GetInfrastructureOverview()
        {
            return new List<InfrastructureItem>
            {
                new InfrastructureItem
                {
                    Service = "Table Storage",
                    ResourceName = "Customers, Products, Orders",
                    Status = "Online"
                },
                new InfrastructureItem
                {
                    Service = "Blob Storage",
                    ResourceName = "product-images",
                    Status = "Online"
                },
                new InfrastructureItem
                {
                    Service = "Queue Storage",
                    ResourceName = "order-processing-queue",
                    Status = "Online"
                },
                new InfrastructureItem
                {
                    Service = "File Storage",
                    ResourceName = "logs-share",
                    Status = "Online"
                },
                new InfrastructureItem
                {
                    Service = "App Service",
                    ResourceName = "st10531590",
                    Status = "Online"
                }
            };
        }
    }

    public class InfrastructureItem
    {
        public string? Service { get; set; }
        public string? ResourceName { get; set; }
        public string? Status { get; set; }
    }
}