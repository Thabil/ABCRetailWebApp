using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Services;

namespace ABCRetailWebApp.Controllers
{
    public class LogsController : Controller
    {
        private readonly IFileStorageService _fileService;

        public LogsController(IFileStorageService fileService)
        {
            _fileService = fileService;
        }

        public async Task<IActionResult> Index()
        {
            var files = await _fileService.ListLogFilesAsync();
            return View(files);
        }

        public async Task<IActionResult> Download(string filename)
        {
            var content = await _fileService.DownloadLogFileAsync(filename);
            return Content(content, "text/plain");
        }
    }
}