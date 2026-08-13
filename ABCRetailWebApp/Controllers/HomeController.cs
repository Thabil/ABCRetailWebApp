using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Services;

namespace ABCRetailWebApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly AuthService _authService;

        public HomeController(AuthService authService)
        {
            _authService = authService;
        }

        public IActionResult Index()
        {
            if (_authService.IsLoggedIn(HttpContext))
            {
                var user = _authService.GetUserFromSession(HttpContext);
                if (user?.UserType == "Admin")
                {
                    return RedirectToAction("Dashboard", "Admin");
                }
                return RedirectToAction("Index", "Products");
            }
            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Login()
        {
            return RedirectToAction("Login", "Account");
        }

        public IActionResult SignUp()
        {
            return RedirectToAction("Register", "Account");
        }
    }
}