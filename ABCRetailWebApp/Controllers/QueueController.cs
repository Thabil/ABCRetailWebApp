using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Services;

namespace ABCRetailWebApp.Controllers
{
    public class QueueController : Controller
    {
        private readonly IQueueStorageService _queueService;

        public QueueController(IQueueStorageService queueService)
        {
            _queueService = queueService;
        }

        public async Task<IActionResult> Index()
        {
            var messages = await _queueService.PeekMessagesAsync("product-events", 10);
            return View(messages);
        }

        public async Task<IActionResult> CustomerEvents()
        {
            var messages = await _queueService.PeekMessagesAsync("customer-events", 10);
            return View("Index", messages);
        }
    }
}