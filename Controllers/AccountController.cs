using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EmekliRehberi.Data;
using EmekliRehberi.Models;
using EmekliRehberi.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmekliRehberi.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;

        public AccountController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var exists = await _context.Users
                .AnyAsync(x => x.Email == model.Email || x.TcKimlikNo == model.TcKimlikNo);

            if (exists)
            {
                ModelState.AddModelError("", "Bu e-posta veya T.C. kimlik numarası ile kayıt bulunmaktadır.");
                return View(model);
            }

            var user = new AppUser
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                TcKimlikNo = model.TcKimlikNo,
                Email = model.Email,
                PasswordHash = HashPassword(model.Password),
                Role = "Vatandas" // Rol her zaman sabit; formdan alınmaz.
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

           var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim(ClaimTypes.Role, user.Role)
};

var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
var principal = new ClaimsPrincipal(identity);

await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

if (user.Role == "Vatandas")
    return RedirectToAction("Index", "Citizen");

if (user.Role == "Memur")
    return RedirectToAction("Index", "Officer");

return RedirectToAction("Index", "Admin");
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View(new LoginViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var passwordHash = HashPassword(model.Password);

            var user = await _context.Users.FirstOrDefaultAsync(x =>
                (x.Email == model.TcKimlikNoOrEmail || x.TcKimlikNo == model.TcKimlikNoOrEmail)
                && x.PasswordHash == passwordHash);

            if (user == null)
            {
                ModelState.AddModelError("", "Kullanıcı bilgileri hatalı.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            if (user.Role == "Vatandas")
                return RedirectToAction("Index", "Citizen");

            if (user.Role == "Memur")
                return RedirectToAction("Index", "Officer");

            return RedirectToAction("Index", "Admin");
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        private string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}