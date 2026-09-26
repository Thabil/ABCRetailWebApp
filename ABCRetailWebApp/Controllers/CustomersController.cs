using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using ABCRetailWebApp.Services;

namespace ABCRetailWebApp.Controllers
{
    public class CustomersController : Controller
    {
        private readonly ITableStorageService<CustomerEntity> _customerService;
        private readonly IQueueStorageService _queueService;
        private readonly IFileStorageService _fileService;

        public CustomersController(
            ITableStorageService<CustomerEntity> customerService,
            IQueueStorageService queueService,
            IFileStorageService fileService)
        {
            _customerService = customerService;
            _queueService = queueService;
            _fileService = fileService;
        }

        public async Task<IActionResult> Index()
        {
            var customers = await _customerService.GetEntitiesAsync();
            return View(customers);
        }

        public IActionResult Create() => View();

        [HttpPost]
        public async Task<IActionResult> Create(CustomerEntity customer)
        {
            if (string.IsNullOrEmpty(customer.RowKey))
                customer.RowKey = Guid.NewGuid().ToString();
            if (string.IsNullOrEmpty(customer.PartitionKey))
                customer.PartitionKey = "default";

            ModelState.Remove("RowKey");
            ModelState.Remove("PartitionKey");
            ModelState.Remove("Timestamp");
            ModelState.Remove("ETag");

            if (!ModelState.IsValid) return View(customer);

            try
            {
                customer.CreatedAt = DateTime.UtcNow;
                customer.IsActive = true;
                await _customerService.AddEntityAsync(customer);

                //  Send to Queue
                var queueMessage = $"New customer created: {customer.FirstName} {customer.LastName}, Email: {customer.Email}";
                await _queueService.SendMessageAsync("customer-events", queueMessage);

                //  Log to File Storage
                await _fileService.AppendToLogAsync($"customers-{DateTime.Now:yyyy-MM-dd}.log",
                    $"[{DateTime.UtcNow}] Created customer: {customer.FirstName} {customer.LastName} - {customer.Email}");

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error: {ex.Message}");
                return View(customer);
            }
        }

        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return BadRequest();
            var customer = await _customerService.GetEntityAsync("default", id);
            if (customer == null) return NotFound();
            return View(customer);
        }

        [HttpPost, ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            try
            {
                var customer = await _customerService.GetEntityAsync("default", id);
                if (customer != null)
                {
                    await _customerService.DeleteEntityAsync("default", id);

                    // ✅ Send to Queue
                    await _queueService.SendMessageAsync("customer-events",
                        $"Customer deleted: {customer.FirstName} {customer.LastName} - {customer.Email}");

                    // ✅ Log to File Storage
                    await _fileService.AppendToLogAsync($"customers-{DateTime.Now:yyyy-MM-dd}.log",
                        $"[{DateTime.UtcNow}] Deleted customer: {customer.FirstName} {customer.LastName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Delete Error: {ex.Message}");
            }
            return RedirectToAction(nameof(Index));
        }
    }
}