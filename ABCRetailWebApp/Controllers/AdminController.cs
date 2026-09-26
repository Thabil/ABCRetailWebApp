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
        private readonly ITableStorageService<ABCRetailWebApp.Models.OrderAuditEntity> _orderAuditService;
        private readonly ITableStorageService<ABCRetailWebApp.Models.StockAlertEntity> _stockAlertService;
        private readonly ITableStorageService<ABCRetailWebApp.Models.TransactionLogEntity> _transactionService;
        private readonly IBlobStorageService _blobService;
        private readonly IQueueStorageService _queueService;
        private readonly IFileStorageService _fileService;
        private readonly FunctionHttpClient _functionClient;

        public AdminController(
            AuthService authService,
            ITableStorageService<CustomerEntity> customerService,
            ITableStorageService<ProductEntity> productService,
            ITableStorageService<ABCRetailWebApp.Models.OrderEntity> orderService,
            ITableStorageService<ABCRetailWebApp.Models.OrderAuditEntity> orderAuditService,
            ITableStorageService<ABCRetailWebApp.Models.StockAlertEntity> stockAlertService,
            ITableStorageService<ABCRetailWebApp.Models.TransactionLogEntity> transactionService,
            IBlobStorageService blobService,
            IQueueStorageService queueService,
            IFileStorageService fileService,
            FunctionHttpClient functionClient)
        {
            _authService = authService;
            _customerService = customerService;
            _productService = productService;
            _orderService = orderService;
            _orderAuditService = orderAuditService;
            _stockAlertService = stockAlertService;
            _transactionService = transactionService;
            _blobService = blobService;
            _queueService = queueService;
            _fileService = fileService;
            _functionClient = functionClient;
        }

        // ============================================================
        // ADMIN DASHBOARD
        // ============================================================

        public async Task<IActionResult> Dashboard()
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            var customers = await _customerService.GetEntitiesAsync();
            var products  = await _productService.GetEntitiesAsync();
            var orders    = await _orderService.GetEntitiesAsync();
            var stockAlerts = await _stockAlertService.GetEntitiesAsync();

            var viewModel = new ABCRetailWebApp.Models.AdminDashboardViewModel
            {
                TotalCustomers  = customers.Count(),
                InventoryItems  = products.Count(),
                TotalOrders     = orders.Count(),
                PendingOrders   = orders.Count(o => o.OrderStatus == "Processing" || o.OrderStatus == "Pending"),
                StockAlertCount = stockAlerts.Count(),
                AuditLogEntries = await GetLogCountAsync(),
                TotalRevenue    = orders.Sum(o => o.TotalAmount),
                AllOrders       = orders.OrderByDescending(o => o.CreatedAt).ToList()
            };

            ViewBag.Infrastructure = GetInfrastructureOverview();
            return View(viewModel);
        }

        // ============================================================
        // ADMIN - ORDERS (Consolidated View)
        // ============================================================

        public async Task<IActionResult> Orders()
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            var orders = await _orderService.GetEntitiesAsync();
            return View(orders.OrderByDescending(o => o.CreatedAt));
        }

        // ============================================================
        // ADMIN - SHIP ORDER
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> ShipOrder(string partitionKey, string rowKey)
        {
            if (!_authService.IsAdmin(HttpContext))
                return Json(new { success = false, message = "Unauthorised" });

            var order = await _orderService.GetEntityAsync(partitionKey, rowKey);
            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            order.OrderStatus = "Shipped";
            order.ShippedAt   = DateTime.UtcNow;
            await _orderService.UpdateEntityAsync(order);

            await _fileService.AppendToLogAsync($"queue-{DateTime.Now:yyyy-MM-dd}.log",
                $"[{DateTime.UtcNow}] Order {order.OrderId} marked Shipped by admin");

            return Json(new { success = true, message = $"{order.OrderId} marked as Shipped" });
        }

        // ============================================================
        // ADMIN - FUNCTION ACTIVITY
        // ============================================================

        public async Task<IActionResult> FunctionActivity()
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            var auditTask        = _orderAuditService.GetEntitiesAsync();
            var stockAlertsTask  = _stockAlertService.GetEntitiesAsync();
            var transactionsTask = _transactionService.GetEntitiesAsync();

            await Task.WhenAll(auditTask, stockAlertsTask, transactionsTask);

            var vm = new ABCRetailWebApp.Models.FunctionActivityViewModel
            {
                OrderAudit   = (await auditTask).OrderByDescending(e => e.Timestamp).ToList(),
                StockAlerts  = (await stockAlertsTask).OrderByDescending(e => e.LastAlertTime).ToList(),
                Transactions = (await transactionsTask).OrderByDescending(e => e.Timestamp).ToList()
            };

            return View(vm);
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
        // ADMIN - ORDER QUEUE (redirects to Orders management page)
        // ============================================================

        public IActionResult Queue() => RedirectToAction("Orders");

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

            // Parse OrderId from JSON event format: {"orderId":"ORD-xxx",...}
            string? orderId = null;
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(messageText);
                if (doc.RootElement.TryGetProperty("orderId", out var idProp))
                    orderId = idProp.GetString();
            }
            catch { /* non-JSON message — orderId stays null */ }

            if (orderId != null)
            {
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

            return Json(new { success = true, message = $"Processed: {orderId ?? "unknown"} → Shipped" });
        }

        // ============================================================
        // ADMIN - PURGE QUEUE
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> PurgeQueue()
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            await _queueService.ClearQueueAsync("order-processing-queue");
            await _queueService.ClearQueueAsync("order-processing-queue-poison");

            await _fileService.AppendToLogAsync($"queue-{DateTime.Now:yyyy-MM-dd}.log",
                $"[{DateTime.UtcNow}] Queue and poison queue purged by admin");

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
                return RedirectToAction("AccessDenied", "Account");

            try
            {
                product.RowKey       = Guid.NewGuid().ToString();
                product.PartitionKey = product.Category ?? "General";
                product.CreatedAt    = DateTime.UtcNow;
                product.IsAvailable  = true;

                // Step 1: Try blob upload via function (3s timeout — falls back to null)
                if (imageFile != null && imageFile.Length > 0)
                {
                    using var form = new System.Net.Http.MultipartFormDataContent();
                    using var ms   = new MemoryStream();
                    await imageFile.CopyToAsync(ms);
                    form.Add(new System.Net.Http.ByteArrayContent(ms.ToArray()), "file", imageFile.FileName);
                    form.Add(new System.Net.Http.StringContent(product.RowKey),          "productId");
                    form.Add(new System.Net.Http.StringContent(imageFile.ContentType),   "contentType");

                    var blobResult = await _functionClient.PostMultipartAsync<BlobUploadResponse>(
                        "/api/BlobUploadFunction", form);

                    // Use function result if available; otherwise ImageBlobName stays null (placeholder shown)
                    product.ImageBlobName = blobResult?.BlobName;
                }

                // Step 2: Direct table write — admin is never blocked by the function
                await _productService.AddEntityAsync(product);

                // Step 3: Fire-and-forget call to TableWriteFunction (audit + queue notification)
                // We do NOT await this — the product is already saved above
                _ = _functionClient.PostJsonAsync<TableWriteResponse>("/api/TableWriteFunction", new
                {
                    partitionKey  = product.PartitionKey,
                    rowKey        = product.RowKey,
                    productName   = product.ProductName,
                    description   = product.Description,
                    price         = product.Price,
                    stockQuantity = product.StockQuantity,
                    category      = product.Category,
                    brand         = product.Brand,
                    size          = product.Size,
                    color         = product.Color,
                    material      = product.Material,
                    imageBlobName = product.ImageBlobName,
                    isAvailable   = product.IsAvailable,
                    createdAt     = product.CreatedAt
                });

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
            return RedirectToAction("Dashboard");
        }

        // ============================================================
        // ADMIN - VERIFY INVENTORY STOCK
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> VerifyStock()
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            var messages = await _queueService.PeekMessagesAsync("stock-alerts-queue", 32);

            var alertedIds = messages
                .Select(m =>
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(m);
                        return doc.RootElement.TryGetProperty("productId", out var p) ? p.GetString() : null;
                    }
                    catch { return null; }
                })
                .Where(id => id != null)
                .ToHashSet();

            var allProducts = await _productService.GetEntitiesAsync();
            var outOfStock = allProducts
                .Where(p => alertedIds.Contains(p.RowKey) && p.StockQuantity < 1)
                .ToList();

            var customers = await _customerService.GetEntitiesAsync();
            var products = allProducts.ToList();
            var orders = await _orderService.GetEntitiesAsync();

            var viewModel = new ABCRetailWebApp.Models.AdminDashboardViewModel
            {
                TotalCustomers = customers.Count(),
                InventoryItems = products.Count(),
                TotalOrders = orders.Count(),
                PendingOrders = orders.Count(o => o.OrderStatus == "Processing" || o.OrderStatus == "Pending"),
                AuditLogEntries = await GetLogCountAsync(),
                TotalRevenue = orders.Sum(o => o.TotalAmount),
                AllOrders = orders.OrderByDescending(o => o.CreatedAt).ToList(),
                OutOfStockProducts = outOfStock
            };

            ViewBag.Infrastructure = GetInfrastructureOverview();
            return View("Dashboard", viewModel);
        }

        // ============================================================
        // ADMIN - UPDATE STOCK
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> UpdateStock(string partitionKey, string rowKey, int quantityToAdd)
        {
            if (!_authService.IsAdmin(HttpContext))
                return RedirectToAction("AccessDenied", "Account");

            if (quantityToAdd <= 10)
            {
                TempData["StockError"] = "Restock quantity must be greater than 10.";
                return RedirectToAction("Dashboard");
            }

            var product = await _productService.GetEntityAsync(partitionKey, rowKey);
            if (product == null)
            {
                TempData["StockError"] = "Product not found.";
                return RedirectToAction("Dashboard");
            }

            product.StockQuantity += quantityToAdd;
            await _productService.UpdateEntityAsync(product);

            await _fileService.AppendToLogAsync($"stock-{DateTime.Now:yyyy-MM-dd}.log",
                $"[{DateTime.UtcNow}] Restocked '{product.ProductName}' +{quantityToAdd} | New stock: {product.StockQuantity}");

            TempData["Success"] = $"'{product.ProductName}' restocked. New quantity: {product.StockQuantity}.";
            return RedirectToAction("Dashboard");
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