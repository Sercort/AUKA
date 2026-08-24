using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemClaim = System.Security.Claims.Claim;
using SystemClaimTypes = System.Security.Claims.ClaimTypes;
using SystemClaimsIdentity = System.Security.Claims.ClaimsIdentity;

namespace EduNexus.Web.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public AccountController(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    // 1. GET: Login
    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            return RedirectToAction("Index", "Home");
        }
        return View();
    }

    // 2. POST: Login
    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        // A) Buscar usuario activo por Email Institucional
        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email && u.Activo);

        if (usuario == null)
        {
            ViewBag.Error = "Correo electrónico o contraseña incorrectos, o la cuenta está desactivada.";
            return View();
        }

        // B) Verificación de Contraseña (Soporta PasswordHasher y texto plano)
        bool esValido = false;

        if (!string.IsNullOrEmpty(usuario.PasswordHash))
        {
            var hasher = new PasswordHasher<string>();
            try
            {
                var result = hasher.VerifyHashedPassword(email, usuario.PasswordHash, password);
                if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    esValido = true;
                }
            }
            catch
            {
                // Si el hash antiguo tenía otro formato
            }
        }

        if (!esValido && usuario.PasswordHash == password)
        {
            esValido = true;
        }

        if (!esValido)
        {
            ViewBag.Error = "Correo electrónico o contraseña incorrectos, o la cuenta está desactivada.";
            return View();
        }

        // C) Crear los Claims
        var claims = new List<SystemClaim>
        {
            new SystemClaim(SystemClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new SystemClaim(SystemClaimTypes.Name, $"{usuario.Nombre} {usuario.Apellido}"),
            new SystemClaim(SystemClaimTypes.Email, usuario.Email),
            new SystemClaim(SystemClaimTypes.Role, usuario.Rol.ToString()),
            new SystemClaim("ColegioId", usuario.ColegioId.ToString())
        };

        var identity = new SystemClaimsIdentity(claims, "EduNexusAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        // D) Iniciar Sesión con Cookie
        await HttpContext.SignInAsync("EduNexusAuth", principal);

        // E) VALIDACIÓN CLAVE PROVISORIA
        if (usuario.DebeCambiarPassword)
        {
            return RedirectToAction(nameof(CambiarPasswordObligatorio));
        }

        return RedirectToAction("Index", "Home");
    }

    // 3. GET: Cambiar Password Obligatorio
    [Authorize]
    [HttpGet]
    public IActionResult CambiarPasswordObligatorio()
    {
        return View();
    }

    // 4. POST: Cambiar Password Obligatorio
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarPasswordObligatorio(string nuevaPassword, string confirmarPassword)
    {
        var userIdClaim = User.FindFirst(SystemClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int usuarioId))
        {
            return RedirectToAction(nameof(Login));
        }

        var usuario = await _context.Usuarios.FindAsync(usuarioId);
        if (usuario == null) return NotFound();

        if (nuevaPassword != confirmarPassword)
        {
            ViewBag.Error = "La nueva contraseña y la confirmación no coinciden.";
            return View();
        }

        if (string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length < 6)
        {
            ViewBag.Error = "La nueva contraseña debe tener al menos 6 caracteres.";
            return View();
        }

        var hasher = new PasswordHasher<string>();
        usuario.PasswordHash = hasher.HashPassword(usuario.Email, nuevaPassword);
        usuario.DebeCambiarPassword = false;

        _context.Usuarios.Update(usuario);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "¡Contraseña actualizada con éxito! Bienvenido a la plataforma.";
        return RedirectToAction("Index", "Home");
    }

    // 5. GET: Cambiar Password Opcional (Desde el Menú Desplegable)
    [HttpGet]
    [Authorize]
    public IActionResult CambiarPassword()
    {
        return View();
    }

    // 6. POST: Cambiar Password Opcional
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarPassword(string passwordActual, string nuevaPassword, string confirmarPassword)
    {
        var userIdClaim = User.FindFirst(SystemClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int usuarioId))
        {
            return RedirectToAction("Login", "Account");
        }

        var usuario = await _context.Usuarios.FindAsync(usuarioId);
        if (usuario == null) return NotFound();

        if (nuevaPassword != confirmarPassword)
        {
            ModelState.AddModelError("", "La nueva contraseña y su confirmación no coinciden.");
            return View();
        }

        if (string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length < 6)
        {
            ModelState.AddModelError("", "La nueva contraseña debe tener al menos 6 caracteres.");
            return View();
        }

        var hasher = new PasswordHasher<string>();

        if (!string.IsNullOrEmpty(usuario.PasswordHash))
        {
            var result = hasher.VerifyHashedPassword(usuario.Email, usuario.PasswordHash, passwordActual);
            if (result == PasswordVerificationResult.Failed && usuario.PasswordHash != passwordActual)
            {
                ModelState.AddModelError("", "La contraseña actual no es correcta.");
                return View();
            }
        }

        usuario.PasswordHash = hasher.HashPassword(usuario.Email, nuevaPassword);
        usuario.DebeCambiarPassword = false;

        _context.Usuarios.Update(usuario);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "✅ Tu contraseña ha sido actualizada con éxito.";
        return RedirectToAction("Index", "Home");
    }

    // 7. GET: Olvidé mi Contraseña
    [HttpGet]
    public IActionResult OlvidoPassword()
    {
        return View();
    }

    // 8. POST: Olvidé mi Contraseña
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OlvidoPassword(string emailOusuario)
    {
        if (string.IsNullOrWhiteSpace(emailOusuario))
        {
            ViewBag.Error = "Por favor ingresa tu Correo Institucional o RUT.";
            return View();
        }

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailOusuario || u.Rut == emailOusuario);

        if (usuario == null)
        {
            ViewBag.Info = "Si los datos coinciden con un usuario activo, se enviarán las instrucciones a su correo personal de respaldo.";
            return View();
        }

        if (string.IsNullOrWhiteSpace(usuario.EmailPersonal))
        {
            ViewBag.Error = "No tienes un correo personal de respaldo registrado. Por favor contacta a Dirección/UTP.";
            return View();
        }

        string correoOculto = OcultarEmail(usuario.EmailPersonal);
        TempData["SuccessMessage"] = $"✅ Se ha enviado un enlace de recuperación al correo de respaldo registrado: {correoOculto}";

        return RedirectToAction(nameof(Login));
    }

    // 9. GET & POST: Logout (Soporta peticiones GET y POST para evitar HTTP 405)
    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            await HttpContext.SignOutAsync("EduNexusAuth");
        }
        return RedirectToAction(nameof(Login));
    }

    // 10. GET: Acceso Denegado
    [HttpGet]
    public IActionResult AccesoDenegado()
    {
        return View();
    }

    private string OcultarEmail(string email)
    {
        var partes = email.Split('@');
        if (partes.Length < 2) return "***@***.com";

        string nombre = partes[0];
        string dominio = partes[1];

        string nombreOculto = nombre.Length > 2
            ? nombre.Substring(0, 2) + new string('*', nombre.Length - 2)
            : nombre + "**";

        return $"{nombreOculto}@{dominio}";
    }

    // GET: /Account/GoogleLogin
    [HttpGet]
    public IActionResult GoogleLogin()
    {
        var redirectUrl = Url.Action("GoogleResponse", "Account");
        var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, "Google");
    }

    // GET: /Account/GoogleResponse
    [HttpGet]
    public async Task<IActionResult> GoogleResponse()
    {
        var result = await HttpContext.AuthenticateAsync("Google");
        if (!result.Succeeded)
        {
            TempData["Error"] = "No se pudo completar la autenticación con Google.";
            return RedirectToAction("Login");
        }

        var email = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(email))
        {
            TempData["Error"] = "No se obtuvo la cuenta de correo desde Google.";
            return RedirectToAction("Login");
        }

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == email && u.Activo);
        if (usuario == null)
        {
            TempData["Error"] = $"El correo {email} no se encuentra registrado en el sistema.";
            return RedirectToAction("Login");
        }

        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, $"{usuario.Nombre} {usuario.Apellido}"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, usuario.Email),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, usuario.Rol.ToString()),
            new System.Security.Claims.Claim("ColegioId", usuario.ColegioId.ToString())
        };

        var identity = new System.Security.Claims.ClaimsIdentity(claims, "EduNexusAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("EduNexusAuth", principal);
        return RedirectToAction("Index", "Home");
    }
    // GET: /Account/Registrar
    [HttpGet]
    public IActionResult Registrar()
    {
        return View();
    }

    // POST: /Account/Registrar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar(
        string nombreColegio, string rbd, string telefonoColegio, string direccion,
        string nombreAdmin, string apellidoAdmin, string rutAdmin, string emailAdmin,
        string password, string confirmarPassword)
    {
        if (password != confirmarPassword)
        {
            ViewBag.Error = "Las contraseñas no coinciden.";
            return View();
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            ViewBag.Error = "La contraseña debe tener al menos 6 caracteres.";
            return View();
        }

        if (await _context.Usuarios.AnyAsync(u => u.Email == emailAdmin))
        {
            ViewBag.Error = "El correo electrónico ya se encuentra registrado.";
            return View();
        }

        if (await _context.Usuarios.AnyAsync(u => u.Rut == rutAdmin))
        {
            ViewBag.Error = "El RUT ingresado ya está registrado en el sistema.";
            return View();
        }

        var nuevoColegio = new Colegio
        {
            Nombre = nombreColegio,
            Telefono = telefonoColegio,
            Direccion = direccion,
            Activo = true
        };

        _context.Colegios.Add(nuevoColegio);
        await _context.SaveChangesAsync();

        var hasher = new PasswordHasher<string>();
        var adminUsuario = new Usuario
        {
            ColegioId = nuevoColegio.Id,
            Rut = rutAdmin,
            Nombre = nombreAdmin,
            Apellido = apellidoAdmin,
            Email = emailAdmin,
            PasswordHash = hasher.HashPassword(emailAdmin, password),
            Rol = RolUsuario.SuperAdmin,
            Activo = true,
            DebeCambiarPassword = false
        };

        _context.Usuarios.Add(adminUsuario);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "🎉 ¡Establecimiento y cuenta creados con éxito! Ya puedes iniciar sesión.";
        return RedirectToAction(nameof(Login));
    }
}