using ABCRetailWebApp.Controllers;
using ABCRetailWebApp.Models;
using ABCRetailWebApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using System.Text.Json;

namespace ABCRetailWebApp.Tests
{
    // ============================================================
    // HELPERS
    // ============================================================

    public static class ControllerTestHelper
    {
        public static void SetupHttpContext(Controller controller, Dictionary<string, string>? sessionValues = null)
        {
            var session = new MockSession();
            if (sessionValues != null)
                foreach (var kv in sessionValues)
                    session.SetString(kv.Key, kv.Value);

            var httpContext = new DefaultHttpContext { Session = session };
            var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = tempData;
        }

        public static string SerialiseCart(List<CartItem> cart) =>
            JsonSerializer.Serialize(cart);

        /// <summary>Serialises a user into the session the same way AuthService.SetUserSession does.</summary>
        public static string SerialiseUser(CustomerEntity user) =>
            JsonSerializer.Serialize(new
            {
                user.Email,
                user.FirstName,
                user.LastName,
                user.UserType,
                user.RowKey,
                user.PartitionKey
            });
    }

    public class MockSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }

    // ============================================================
    // ADDTOCART VALIDATION TESTS
    // ============================================================

    public class AddToCartValidationTests
    {
        private readonly Mock<ITableStorageService<ProductEntity>> _productSvc = new();
        private readonly Mock<ITableStorageService<ABCRetailWebApp.Models.OrderEntity>> _orderSvc = new();
        private readonly Mock<IBlobStorageService> _blobSvc = new();
        private readonly Mock<IQueueStorageService> _queueSvc = new();
        private readonly Mock<IFileStorageService> _fileSvc = new();
        private readonly Mock<ITableStorageService<CustomerEntity>> _customerSvc = new();

        private ProductsController BuildController()
        {
            var authSvc = new AuthService(_customerSvc.Object);
            return new ProductsController(_productSvc.Object, _orderSvc.Object, _blobSvc.Object,
                _queueSvc.Object, _fileSvc.Object, authSvc);
        }

        [Fact]
        public async Task AddToCart_QuantityExceedsStock_SetsTempDataError()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-1", PartitionKey = "Clothing",
                ProductName = "Test Shirt", StockQuantity = 3, Price = 99.99
            };
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity> { product });

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller);

            var result = await controller.AddToCart("prod-1", 5);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", ((RedirectToActionResult)result).ActionName);
            Assert.NotNull(controller.TempData["CartError"]);
            Assert.Contains("3", controller.TempData["CartError"]!.ToString());
        }

        [Fact]
        public async Task AddToCart_QuantityWithinStock_AddsToCart()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-2", PartitionKey = "Clothing",
                ProductName = "Test Hoodie", StockQuantity = 10, Price = 199.99
            };
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity> { product });

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller);

            var result = await controller.AddToCart("prod-2", 2);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", ((RedirectToActionResult)result).ActionName);
            Assert.Null(controller.TempData["CartError"]);
        }

        [Fact]
        public async Task AddToCart_ExistingCartQtyPlusNewExceedsStock_SetsTempDataError()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-3", PartitionKey = "Clothing",
                ProductName = "Test Jacket", StockQuantity = 4, Price = 299.99
            };
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity> { product });

            var existingCart = new List<CartItem>
            {
                new CartItem { ProductId = "prod-3", ProductName = "Test Jacket", Quantity = 3, Price = 299.99 }
            };

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller, new Dictionary<string, string>
            {
                ["Cart"] = ControllerTestHelper.SerialiseCart(existingCart)
            });

            var result = await controller.AddToCart("prod-3", 2);

            Assert.NotNull(controller.TempData["CartError"]);
        }

        [Fact]
        public async Task AddToCart_ProductNotFound_RedirectsWithoutError()
        {
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity>());

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller);

            var result = await controller.AddToCart("nonexistent", 1);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", ((RedirectToActionResult)result).ActionName);
        }
    }

    // ============================================================
    // PROCESSPAYMENT — STOCK DECREMENT & QUEUE TESTS
    // ============================================================

    public class ProcessPaymentStockTests
    {
        private readonly Mock<ITableStorageService<ProductEntity>> _productSvc = new();
        private readonly Mock<ITableStorageService<ABCRetailWebApp.Models.OrderEntity>> _orderSvc = new();
        private readonly Mock<IBlobStorageService> _blobSvc = new();
        private readonly Mock<IQueueStorageService> _queueSvc = new();
        private readonly Mock<IFileStorageService> _fileSvc = new();
        private readonly Mock<ITableStorageService<CustomerEntity>> _customerSvc = new();

        private ProductsController BuildController() =>
            new(_productSvc.Object, _orderSvc.Object, _blobSvc.Object,
                _queueSvc.Object, _fileSvc.Object, new AuthService(_customerSvc.Object));

        private CustomerEntity FakeUser() => new CustomerEntity
        {
            RowKey = "user-1", PartitionKey = "Customer",
            FirstName = "Test", LastName = "User",
            Email = "test@example.com", UserType = "Customer"
        };

        private Dictionary<string, string> SessionWithUser(CustomerEntity user, List<CartItem> cart) =>
            new()
            {
                ["User"] = ControllerTestHelper.SerialiseUser(user),
                ["IsLoggedIn"] = "true",
                ["Cart"] = ControllerTestHelper.SerialiseCart(cart)
            };

        [Fact]
        public async Task ProcessPayment_DecrementsStockForEachCartItem()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-10", PartitionKey = "Clothing",
                ProductName = "Decrement Shirt", StockQuantity = 5, Price = 100
            };

            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity> { product });
            _productSvc.Setup(s => s.UpdateEntityAsync(It.IsAny<ProductEntity>())).Returns(Task.CompletedTask);
            _orderSvc.Setup(s => s.AddEntityAsync(It.IsAny<ABCRetailWebApp.Models.OrderEntity>())).Returns(Task.CompletedTask);
            _queueSvc.Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            _fileSvc.Setup(s => s.AppendToLogAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var cart = new List<CartItem>
            {
                new CartItem { ProductId = "prod-10", ProductName = "Decrement Shirt", Quantity = 3, Price = 100 }
            };

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller, SessionWithUser(FakeUser(), cart));

            await controller.ProcessPayment("4111111111111111", "12/26", "123");

            _productSvc.Verify(s => s.UpdateEntityAsync(
                It.Is<ProductEntity>(p => p.RowKey == "prod-10" && p.StockQuantity == 2)), Times.Once);
        }

        [Fact]
        public async Task ProcessPayment_WhenStockHitsZero_SendsOutOfStockQueueMessage()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-11", PartitionKey = "Clothing",
                ProductName = "Last Item Shirt", Category = "Clothing",
                StockQuantity = 2, Price = 100
            };

            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity> { product });
            _productSvc.Setup(s => s.UpdateEntityAsync(It.IsAny<ProductEntity>())).Returns(Task.CompletedTask);
            _orderSvc.Setup(s => s.AddEntityAsync(It.IsAny<ABCRetailWebApp.Models.OrderEntity>())).Returns(Task.CompletedTask);
            _queueSvc.Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            _fileSvc.Setup(s => s.AppendToLogAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var cart = new List<CartItem>
            {
                new CartItem { ProductId = "prod-11", ProductName = "Last Item Shirt", Quantity = 2, Price = 100 }
            };

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller, SessionWithUser(FakeUser(), cart));

            await controller.ProcessPayment("4111111111111111", "12/26", "123");

            _queueSvc.Verify(s => s.SendMessageAsync(
                "stock-alerts-queue",
                It.Is<string>(m => m.Contains("prod-11") && m.Contains("OutOfStock"))),
                Times.Once);
        }

        [Fact]
        public async Task ProcessPayment_WhenStockRemainsAboveZero_DoesNotSendStockAlert()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-12", PartitionKey = "Clothing",
                ProductName = "Plenty Shirt", Category = "Clothing",
                StockQuantity = 10, Price = 100
            };

            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity> { product });
            _productSvc.Setup(s => s.UpdateEntityAsync(It.IsAny<ProductEntity>())).Returns(Task.CompletedTask);
            _orderSvc.Setup(s => s.AddEntityAsync(It.IsAny<ABCRetailWebApp.Models.OrderEntity>())).Returns(Task.CompletedTask);
            _queueSvc.Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            _fileSvc.Setup(s => s.AppendToLogAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var cart = new List<CartItem>
            {
                new CartItem { ProductId = "prod-12", ProductName = "Plenty Shirt", Quantity = 2, Price = 100 }
            };

            var controller = BuildController();
            ControllerTestHelper.SetupHttpContext(controller, SessionWithUser(FakeUser(), cart));

            await controller.ProcessPayment("4111111111111111", "12/26", "123");

            _queueSvc.Verify(s => s.SendMessageAsync("stock-alerts-queue", It.IsAny<string>()), Times.Never);
        }
    }

    // ============================================================
    // ADMIN — UPDATESTOCK VALIDATION TESTS
    // ============================================================

    public class UpdateStockTests
    {
        private readonly Mock<ITableStorageService<CustomerEntity>> _customerSvc = new();
        private readonly Mock<ITableStorageService<ProductEntity>> _productSvc = new();
        private readonly Mock<ITableStorageService<ABCRetailWebApp.Models.OrderEntity>> _orderSvc = new();
        private readonly Mock<IBlobStorageService> _blobSvc = new();
        private readonly Mock<IQueueStorageService> _queueSvc = new();
        private readonly Mock<IFileStorageService> _fileSvc = new();

        private static FunctionHttpClient OfflineFunctionClient()
        {
            var http = new HttpClient(new OfflineHttpHandler()) { BaseAddress = new Uri("http://localhost:7071") };
            return new FunctionHttpClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<FunctionHttpClient>.Instance);
        }

        private AdminController BuildController()
        {
            var authSvc = new AuthService(_customerSvc.Object);
            return new AdminController(
                authSvc, _customerSvc.Object, _productSvc.Object,
                _orderSvc.Object, _blobSvc.Object, _queueSvc.Object, _fileSvc.Object, OfflineFunctionClient());
        }

        private void SetupAdminSession(Controller controller)
        {
            var admin = new CustomerEntity
            {
                RowKey = "admin-1", PartitionKey = "Admin",
                FirstName = "Admin", LastName = "User",
                Email = "admin@example.com", UserType = "Admin"
            };
            ControllerTestHelper.SetupHttpContext(controller, new Dictionary<string, string>
            {
                ["User"] = ControllerTestHelper.SerialiseUser(admin),
                ["IsLoggedIn"] = "true"
            });
        }

        [Fact]
        public async Task UpdateStock_QuantityTenOrLess_SetsTempDataError()
        {
            var controller = BuildController();
            SetupAdminSession(controller);

            var result = await controller.UpdateStock("Clothing", "prod-1", 10);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.NotNull(controller.TempData["StockError"]);
            Assert.Contains("greater than 10", controller.TempData["StockError"]!.ToString());
        }

        [Fact]
        public async Task UpdateStock_ValidQuantity_UpdatesStockAndWritesLog()
        {
            var product = new ProductEntity
            {
                RowKey = "prod-20", PartitionKey = "Clothing",
                ProductName = "Restock Shirt", StockQuantity = 0
            };

            _productSvc.Setup(s => s.GetEntityAsync("Clothing", "prod-20")).ReturnsAsync(product);
            _productSvc.Setup(s => s.UpdateEntityAsync(It.IsAny<ProductEntity>())).Returns(Task.CompletedTask);
            _fileSvc.Setup(s => s.AppendToLogAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var controller = BuildController();
            SetupAdminSession(controller);

            var result = await controller.UpdateStock("Clothing", "prod-20", 20);

            _productSvc.Verify(s => s.UpdateEntityAsync(
                It.Is<ProductEntity>(p => p.StockQuantity == 20)), Times.Once);
            _fileSvc.Verify(s => s.AppendToLogAsync(
                It.Is<string>(f => f.StartsWith("stock-")),
                It.Is<string>(m => m.Contains("Restock Shirt") && m.Contains("+20"))),
                Times.Once);
            Assert.NotNull(controller.TempData["Success"]);
        }

        [Fact]
        public async Task UpdateStock_ProductNotFound_SetsTempDataError()
        {
            _productSvc.Setup(s => s.GetEntityAsync(It.IsAny<string>(), It.IsAny<string>()))
                       .ReturnsAsync((ProductEntity?)null);

            var controller = BuildController();
            SetupAdminSession(controller);

            var result = await controller.UpdateStock("Clothing", "ghost-id", 15);

            Assert.NotNull(controller.TempData["StockError"]);
            Assert.Contains("not found", controller.TempData["StockError"]!.ToString());
        }
    }

    // ============================================================
    // ADMIN — VERIFYSTOCK CROSS-REFERENCE TESTS
    // ============================================================

    public class VerifyStockTests
    {
        private readonly Mock<ITableStorageService<CustomerEntity>> _customerSvc = new();
        private readonly Mock<ITableStorageService<ProductEntity>> _productSvc = new();
        private readonly Mock<ITableStorageService<ABCRetailWebApp.Models.OrderEntity>> _orderSvc = new();
        private readonly Mock<IBlobStorageService> _blobSvc = new();
        private readonly Mock<IQueueStorageService> _queueSvc = new();
        private readonly Mock<IFileStorageService> _fileSvc = new();

        private static FunctionHttpClient OfflineFunctionClient()
        {
            var http = new HttpClient(new OfflineHttpHandler()) { BaseAddress = new Uri("http://localhost:7071") };
            return new FunctionHttpClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<FunctionHttpClient>.Instance);
        }

        private AdminController BuildController()
        {
            var authSvc = new AuthService(_customerSvc.Object);
            var controller = new AdminController(
                authSvc, _customerSvc.Object, _productSvc.Object,
                _orderSvc.Object, _blobSvc.Object, _queueSvc.Object, _fileSvc.Object, OfflineFunctionClient());

            _customerSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<CustomerEntity>());
            _orderSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ABCRetailWebApp.Models.OrderEntity>());
            _fileSvc.Setup(s => s.ListLogFilesAsync()).ReturnsAsync(new List<string>());

            return controller;
        }

        private void SetupAdminSession(Controller controller)
        {
            var admin = new CustomerEntity
            {
                RowKey = "admin-1", PartitionKey = "Admin",
                FirstName = "Admin", LastName = "User",
                Email = "admin@example.com", UserType = "Admin"
            };
            ControllerTestHelper.SetupHttpContext(controller, new Dictionary<string, string>
            {
                ["User"] = ControllerTestHelper.SerialiseUser(admin),
                ["IsLoggedIn"] = "true"
            });
        }

        [Fact]
        public async Task VerifyStock_OnlyReturnsProductsStillAtZeroStock()
        {
            var queueMessages = new List<string>
            {
                "{\"eventType\":\"OutOfStock\",\"productId\":\"prod-A\",\"productName\":\"Shirt A\",\"category\":\"Clothing\",\"remainingStock\":0,\"timestamp\":\"2026-09-23T00:00:00Z\"}",
                "{\"eventType\":\"OutOfStock\",\"productId\":\"prod-B\",\"productName\":\"Shirt B\",\"category\":\"Clothing\",\"remainingStock\":0,\"timestamp\":\"2026-09-23T00:00:00Z\"}"
            };
            var products = new List<ProductEntity>
            {
                new ProductEntity { RowKey = "prod-A", PartitionKey = "Clothing", ProductName = "Shirt A", StockQuantity = 0 },
                new ProductEntity { RowKey = "prod-B", PartitionKey = "Clothing", ProductName = "Shirt B", StockQuantity = 5 }
            };

            _queueSvc.Setup(s => s.PeekMessagesAsync("stock-alerts-queue", 32)).ReturnsAsync(queueMessages);
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(products);

            var controller = BuildController();
            SetupAdminSession(controller);

            var result = await controller.VerifyStock();

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<AdminDashboardViewModel>(viewResult.Model);
            Assert.Single(model.OutOfStockProducts);
            Assert.Equal("prod-A", model.OutOfStockProducts[0].RowKey);
        }

        [Fact]
        public async Task VerifyStock_EmptyQueue_ReturnsEmptyOutOfStockList()
        {
            _queueSvc.Setup(s => s.PeekMessagesAsync("stock-alerts-queue", 32)).ReturnsAsync(new List<string>());
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity>());

            var controller = BuildController();
            SetupAdminSession(controller);

            var result = await controller.VerifyStock();

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<AdminDashboardViewModel>(viewResult.Model);
            Assert.Empty(model.OutOfStockProducts);
        }

        [Fact]
        public async Task VerifyStock_ReturnsDashboardView()
        {
            _queueSvc.Setup(s => s.PeekMessagesAsync("stock-alerts-queue", 32)).ReturnsAsync(new List<string>());
            _productSvc.Setup(s => s.GetEntitiesAsync(null)).ReturnsAsync(new List<ProductEntity>());

            var controller = BuildController();
            SetupAdminSession(controller);

            var result = await controller.VerifyStock();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Dashboard", viewResult.ViewName);
        }
    }

    // ============================================================
    // ADMIN — ADDPRODUCT VIA FUNCTION TESTS
    // ============================================================

    public class AddProductFunctionTests
    {
        private readonly Mock<ITableStorageService<CustomerEntity>> _customerSvc = new();
        private readonly Mock<ITableStorageService<ProductEntity>> _productSvc = new();
        private readonly Mock<ITableStorageService<ABCRetailWebApp.Models.OrderEntity>> _orderSvc = new();
        private readonly Mock<IBlobStorageService> _blobSvc = new();
        private readonly Mock<IQueueStorageService> _queueSvc = new();
        private readonly Mock<IFileStorageService> _fileSvc = new();

        private AdminController BuildController(FunctionHttpClient? functionClient = null)
        {
            var authSvc = new AuthService(_customerSvc.Object);
            var fc = functionClient ?? BuildOfflineFunctionClient();
            return new AdminController(
                authSvc, _customerSvc.Object, _productSvc.Object,
                _orderSvc.Object, _blobSvc.Object, _queueSvc.Object, _fileSvc.Object, fc);
        }

        /// <summary>
        /// Returns a FunctionHttpClient backed by an HttpClient that immediately
        /// returns 503 — simulates functions being offline.
        /// </summary>
        private static FunctionHttpClient BuildOfflineFunctionClient()
        {
            var handler = new OfflineHttpHandler();
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7071") };
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<FunctionHttpClient>.Instance;
            return new FunctionHttpClient(http, logger);
        }

        private void SetupAdminSession(Controller controller)
        {
            var admin = new CustomerEntity
            {
                RowKey = "admin-1", PartitionKey = "Admin",
                FirstName = "Admin", LastName = "User",
                Email = "admin@example.com", UserType = "Admin"
            };
            ControllerTestHelper.SetupHttpContext(controller, new Dictionary<string, string>
            {
                ["User"] = ControllerTestHelper.SerialiseUser(admin),
                ["IsLoggedIn"] = "true"
            });
        }

        [Fact]
        public async Task AddProduct_AlwaysWritesDirectlyToTable_RegardlessOfFunctionState()
        {
            // Functions offline — direct table write must still succeed
            _productSvc.Setup(s => s.AddEntityAsync(It.IsAny<ProductEntity>())).Returns(Task.CompletedTask);

            var controller = BuildController();
            SetupAdminSession(controller);

            var product = new ProductEntity { ProductName = "Resilient Tee", Category = "Clothing", Price = 149.99, StockQuantity = 20 };
            var result = await controller.AddProduct(product, null);

            _productSvc.Verify(s => s.AddEntityAsync(It.IsAny<ProductEntity>()), Times.Once);
            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Inventory", ((RedirectToActionResult)result).ActionName);
        }

        [Fact]
        public async Task AddProduct_WhenFunctionOffline_StillRedirectsWithoutError()
        {
            _productSvc.Setup(s => s.AddEntityAsync(It.IsAny<ProductEntity>())).Returns(Task.CompletedTask);

            var controller = BuildController(); // offline function client
            SetupAdminSession(controller);

            var product = new ProductEntity { ProductName = "Offline Test Tee", Category = "Clothing", Price = 99, StockQuantity = 5 };
            var result = await controller.AddProduct(product, null);

            // Must redirect — no exception thrown, no error page
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Inventory", redirect.ActionName);
            Assert.Null(controller.TempData["Error"]);
        }

        [Fact]
        public async Task AddProduct_NoImageFile_ImageBlobNameIsNull()
        {
            ProductEntity? saved = null;
            _productSvc.Setup(s => s.AddEntityAsync(It.IsAny<ProductEntity>()))
                       .Callback<ProductEntity>(p => saved = p)
                       .Returns(Task.CompletedTask);

            var controller = BuildController();
            SetupAdminSession(controller);

            var product = new ProductEntity { ProductName = "No Image Tee", Category = "Clothing", Price = 50, StockQuantity = 10 };
            await controller.AddProduct(product, null);

            Assert.Null(saved?.ImageBlobName);
        }
    }

    /// <summary>HttpMessageHandler that always returns 503 — used to simulate functions being offline in tests.</summary>
    public class OfflineHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
