using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using ABCRetailWebApp.Services;
using System.Text.Json;

namespace ABCRetailWebApp.Controllers
{
    public class ProductsController : Controller
    {
        private readonly ITableStorageService<ProductEntity> _productService;
        private readonly ITableStorageService<ABCRetailWebApp.Models.OrderEntity> _orderService;   // ✅ Added
        private readonly IBlobStorageService _blobService;
        private readonly IQueueStorageService _queueService;
        private readonly IFileStorageService _fileService;
        private readonly AuthService _authService;

        // ✅ Updated constructor to include _orderService
        public ProductsController(
            ITableStorageService<ProductEntity> productService,
            ITableStorageService<ABCRetailWebApp.Models.OrderEntity> orderService,     // ✅ NEW
            IBlobStorageService blobService,
            IQueueStorageService queueService,
            IFileStorageService fileService,
            AuthService authService)
        {
            _productService = productService;
            _orderService = orderService;                       // ✅ ASSIGN
            _blobService = blobService;
            _queueService = queueService;
            _fileService = fileService;
            _authService = authService;
        }

        // ============================================================
        // PRODUCT LIST (Customer View)
        // ============================================================

        public async Task<IActionResult> Index(string? category)
        {
            var products = await _productService.GetEntitiesAsync(category);
            var productList = new List<(ProductEntity Product, string? SasUrl)>();

            foreach (var product in products)
            {
                string? sasUrl = null;
                if (!string.IsNullOrEmpty(product.ImageBlobName))
                {
                    sasUrl = await _blobService.GetSasUriAsync(product.ImageBlobName!);
                }
                productList.Add((product, sasUrl));
            }

            // Get cart count
            var cart = GetCart();
            ViewBag.CartCount = cart.Sum(i => i.Quantity);
            ViewBag.IsLoggedIn = _authService.IsLoggedIn(HttpContext);

            return View(productList);
        }

        // ============================================================
        // PRODUCT DETAILS
        // ============================================================

        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return BadRequest();

            var products = await _productService.GetEntitiesAsync();
            var product = products.FirstOrDefault(p => p.RowKey == id);

            if (product == null) return NotFound();

            var sasUrl = await _blobService.GetSasUriAsync(product.ImageBlobName);
            ViewBag.SasUrl = sasUrl;

            return View(product);
        }

        // ============================================================
        // ADD TO CART
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> AddToCart(string productId, int quantity)
        {
            if (quantity <= 0) quantity = 1;

            var product = (await _productService.GetEntitiesAsync())
                .FirstOrDefault(p => p.RowKey == productId);

            if (product == null)
                return RedirectToAction("Index");

            var cart = GetCart();
            var existingItem = cart.FirstOrDefault(i => i.ProductId == productId);
            var currentCartQty = existingItem?.Quantity ?? 0;

            if (currentCartQty + quantity > product.StockQuantity)
            {
                TempData["CartError"] = $"Only {product.StockQuantity} unit(s) of '{product.ProductName}' available. You already have {currentCartQty} in your cart.";
                return RedirectToAction("Index");
            }

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = product.RowKey,
                    ProductName = product.ProductName,
                    Price = product.Price,
                    Quantity = quantity,
                    ImageBlobName = product.ImageBlobName
                });
            }

            SaveCart(cart);
            return RedirectToAction("Index");
        }

        // ============================================================
        // VIEW CART
        // ============================================================

        public IActionResult Cart()
        {
            var cart = GetCart();
            var total = cart.Sum(i => i.Total);
            var items = cart.Sum(i => i.Quantity);

            ViewBag.Total = total;
            ViewBag.Items = items;
            ViewBag.Shipping = items > 0 ? 50.0 : 0.0;
            ViewBag.GrandTotal = ViewBag.Total + ViewBag.Shipping;

            return View(cart);
        }

        // ============================================================
        // REMOVE FROM CART
        // ============================================================

        [HttpPost]
        public IActionResult RemoveFromCart(string productId)
        {
            var cart = GetCart();
            cart.RemoveAll(i => i.ProductId == productId);
            SaveCart(cart);
            return RedirectToAction("Cart");
        }

        // ============================================================
        // CLEAR CART
        // ============================================================

        [HttpPost]
        public IActionResult ClearCart()
        {
            SaveCart(new List<CartItem>());
            return RedirectToAction("Cart");
        }

        // ============================================================
        // UPDATE QUANTITY IN CART
        // ============================================================

        [HttpPost]
        public IActionResult UpdateQuantity(string productId, int quantity)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(i => i.ProductId == productId);
            if (item != null)
            {
                if (quantity <= 0)
                    cart.Remove(item);
                else
                    item.Quantity = quantity;
            }
            SaveCart(cart);
            return RedirectToAction("Cart");
        }

        // ============================================================
        // CHECKOUT (Payment Popup)
        // ============================================================

        public IActionResult Checkout()
        {
            if (!_authService.IsLoggedIn(HttpContext))
            {
                return RedirectToAction("Login", "Account");
            }

            var cart = GetCart();
            if (!cart.Any())
            {
                return RedirectToAction("Index");
            }

            var total = cart.Sum(i => i.Total);
            var items = cart.Sum(i => i.Quantity);

            ViewBag.Total = total;
            ViewBag.Items = items;
            ViewBag.Shipping = 50.0;
            ViewBag.GrandTotal = total + 50.0;

            return View(cart);
        }

        // ============================================================
        // PROCESS PAYMENT
        // ============================================================

        public record ProcessPaymentRequest(string CardNumber, string ExpiryDate, string Cvc);

        [HttpPost]
        public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
        {
            var cardNumber = request?.CardNumber;
            var expiryDate = request?.ExpiryDate;
            var cvc        = request?.Cvc;
            try
            {
                var cart = GetCart();
                if (!cart.Any())
                {
                    return Json(new { success = false, message = "Cart is empty" });
                }

                var user = _authService.GetUserFromSession(HttpContext);
                if (user == null)
                {
                    return Json(new { success = false, message = "Please login first" });
                }

                // Calculate totals
                var subTotal = cart.Sum(i => i.Total);
                var shipping = 50.0;
                var total = subTotal + shipping;

                // Create order
                var orderId = $"ORD-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";
                var order = new ABCRetailWebApp.Models.OrderEntity
                {
                    PartitionKey = user.RowKey,
                    RowKey = orderId,
                    OrderId = orderId,
                    CustomerId = user.RowKey,
                    CustomerName = $"{user.FirstName} {user.LastName}",
                    CustomerEmail = user.Email,
                    ShippingAddress = user.DeliveryAddress ?? "N/A",
                    SubTotal = subTotal,
                    ShippingCost = shipping,
                    TotalAmount = total,
                    PaymentMethod = "Credit Card",
                    PaymentStatus = "Paid",
                    OrderStatus = "Processing",
                    OrderItemsJson = JsonSerializer.Serialize(cart.Select(i => new
                    {
                        i.ProductId,
                        i.ProductName,
                        i.Quantity,
                        i.Price
                    }))
                };

                // ✅ CORRECT: Save to Orders table using _orderService
                await _orderService.AddEntityAsync(order);

                // Decrement stock and send out-of-stock alerts
                var allProducts = await _productService.GetEntitiesAsync();
                foreach (var item in cart)
                {
                    var product = allProducts.FirstOrDefault(p => p.RowKey == item.ProductId);
                    if (product == null) continue;

                    product.StockQuantity -= item.Quantity;
                    if (product.StockQuantity < 0) product.StockQuantity = 0;
                    await _productService.UpdateEntityAsync(product);

                    if (product.StockQuantity < 1)
                    {
                        var stockAlertMessage = EventMessageHelper.CreateStockAlertEvent(
                            product.RowKey,
                            product.ProductName,
                            product.Category,
                            product.StockQuantity,
                            DateTime.UtcNow
                        );
                        await _queueService.SendMessageAsync("stock-alerts-queue", stockAlertMessage);
                    }
                }

                // Send to Queue (JSON format)
                var orderMessage = EventMessageHelper.CreateOrderEvent(
                    orderId,
                    user.RowKey,
                    $"{user.FirstName} {user.LastName}",
                    user.Email,
                    (decimal)total,
                    DateTime.UtcNow
                );
                await _queueService.SendMessageAsync("order-processing-queue", orderMessage);

                // Log to File
                await _fileService.AppendToLogAsync($"orders-{DateTime.Now:yyyy-MM-dd}.log",
                    $"[{DateTime.UtcNow}] Order {orderId} placed by {user.Email} | R{total:F2}");

                // Clear cart
                SaveCart(new List<CartItem>());

                return Json(new
                {
                    success = true,
                    message = "Order placed successfully!",
                    orderId = orderId,
                    redirectUrl = Url.Action("MyOrders", "Products")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ============================================================
        // MY ORDERS (Customer Order Tracking)
        // ============================================================

        public async Task<IActionResult> MyOrders()
        {
            if (!_authService.IsLoggedIn(HttpContext))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = _authService.GetUserFromSession(HttpContext);
            if (user == null) return RedirectToAction("Login", "Account");

            // ✅ Get orders for this customer using _orderService (injected)
            var allOrders = await _orderService.GetEntitiesAsync();
            var customerOrders = allOrders.Where(o => o.PartitionKey == user.RowKey)
                                          .OrderByDescending(o => o.CreatedAt)
                                          .ToList();

            return View(customerOrders);
        }

        // ============================================================
        // ORDER DETAILS
        // ============================================================

        public async Task<IActionResult> OrderDetails(string id)
        {
            if (string.IsNullOrEmpty(id)) return BadRequest();

            var allOrders = await _orderService.GetEntitiesAsync();
            var order = allOrders.FirstOrDefault(o => o.RowKey == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // ============================================================
        // CART HELPERS
        // ============================================================

        private List<CartItem> GetCart()
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson))
                return new List<CartItem>();

            return JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();
        }

        private void SaveCart(List<CartItem> cart)
        {
            var cartJson = JsonSerializer.Serialize(cart);
             HttpContext.Session.SetString("Cart", cartJson);
            HttpContext.Session.SetInt32("CartCount", cart.Sum(i => i.Quantity));
        }
    }
}