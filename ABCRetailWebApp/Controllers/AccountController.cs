using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Services;

namespace ABCRetailWebApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly AuthService _authService;

        public AccountController(AuthService authService)
        {
            _authService = authService;
        }

        // ============================================================
        // REGISTER - GET
        // ============================================================

        [HttpGet]
        public IActionResult Register()
        {
            // If already logged in, redirect to home
            if (_authService.IsLoggedIn(HttpContext))
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // ============================================================
        // REGISTER - POST
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string firstName, string lastName, string email, string password, string confirmPassword, string userType = "Customer")
        {
            if (password != confirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match");
                return View();
            }

            var result = await _authService.RegisterAsync(firstName, lastName, email, password, userType);

            if (!result.Success)
            {
                ModelState.AddModelError("", result.Message);
                return View();
            }

            TempData["Success"] = "Registration successful! Please log in.";
            return RedirectToAction("Login");
        }

        // ============================================================
        // LOGIN - GET
        // ============================================================

        [HttpGet]
        public IActionResult Login()
        {
            if (_authService.IsLoggedIn(HttpContext))
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // ============================================================
        // LOGIN - POST
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string returnUrl = "")
        {
            var result = await _authService.LoginAsync(email, password);

            if (!result.Success || result.User == null)
            {
                ModelState.AddModelError("", result.Message);
                return View();
            }

            _authService.SetUserSession(HttpContext, result.User);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Redirect based on role
            if (result.User.UserType == "Admin")
            {
                return RedirectToAction("Dashboard", "Admin");
            }

            return RedirectToAction("Index", "Home");
        }

        // ============================================================
        // LOGOUT
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            _authService.Logout(HttpContext);
            return RedirectToAction("Index", "Home");
        }

        // ============================================================
        // ACCESS DENIED
        // ============================================================

        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}