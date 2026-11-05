using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;
using Auka.Application.Interfaces;
using Auka.Web.Services;
using SystemClaim = System.Security.Claims.Claim;
using SystemClaimTypes = System.Security.Claims.ClaimTypes;
using SystemClaimsIdentity = System.Security.Claims.ClaimsIdentity;

namespace Auka.Web.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ICorreoService _correo;
    private readonly CorreoOptions _correoOpt;

    public AccountController(ApplicationDbContext context, IConfiguration configuration, ICorreoService correo, IOptions<CorreoOptions> correoOpt)
    {
        _context = context;
        _configuration = configuration;
        _correo = correo;
        _correoOpt = correoOpt.Value;
    }

    // Roles que trabajan dentro del módulo Psicosocial
    private static readonly string[] RolesPsicosociales = { "Psicologo", "Psicopedagogo", "TrabajadorSocial", "Orientadora" };

    // 1. GET: Login
    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            var planActivo = User.FindFirst("PlanPsicosocial")?.Value == "true";
            return RedirigirSegunRol(User.FindFirst(SystemClaimTypes.Role)?.Value ?? "", planActivo, User.FindFirst("PlanPremium")?.Value != "false");
        }
        return View();
    }

    // 2. POST: Login
    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        // A) Antes de iniciar sesión no hay colegio en la sesión: se busca en todos los colegios
        email = (email ?? string.Empty).Trim();
        password = password ?? string.Empty;
        var emailNorm = email.ToLower();
        Usuario? usuario;
        if (email.Contains('@'))
        {
            usuario = await _context.Usuarios
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailNorm && u.Activo);
        }
        else
        {
            // Ingreso con RUT (12.345.678-5, 12345678-5 o 123456785): se compara sin puntos ni guion
            var rutNorm = new string(email.Where(ch => char.IsDigit(ch) || ch == 'k' || ch == 'K').ToArray()).ToUpperInvariant();
            var coinciden = rutNorm.Length < 7 ? new List<Usuario>() : await _context.Usuarios
                .IgnoreQueryFilters()
                .Where(u => u.Activo && u.Rut.Replace(".", "").Replace("-", "").ToUpper() == rutNorm)
                .Take(2).ToListAsync();
            if (coinciden.Count > 1)
            {
                // La misma persona tiene cuentas en dos organizaciones: con el RUT no se sabe a cuál quiere entrar
                ViewBag.Error = "Ese RUT tiene más de una cuenta. Ingresa con tu correo.";
                return View();
            }
            usuario = coinciden.FirstOrDefault();
        }

        if (usuario == null)
        {
            ViewBag.Error = "Correo/RUT o contraseña incorrectos, o la cuenta está desactivada.";
            return View();
        }

        // Colegio suspendido por la plataforma: sus usuarios no pueden entrar (los datos se conservan)
        if (usuario.Rol != RolUsuario.AdminPlataforma)
        {
            var colegioActivo = await _context.Colegios.AsNoTracking().Where(c => c.Id == usuario.ColegioId).Select(c => (bool?)c.Activo).FirstOrDefaultAsync();
            if (colegioActivo == false)
            {
                ViewBag.Error = "El acceso de tu colegio está suspendido. Comunícate con AUKA para reactivarlo.";
                return View();
            }
        }

        // B) Verificación de Contraseña (Soporta PasswordHasher y texto plano)
        bool esValido = false;

        if (!string.IsNullOrEmpty(usuario.PasswordHash))
        {
            var hasher = new PasswordHasher<string>();
            try
            {
                var result = hasher.VerifyHashedPassword(usuario.Email, usuario.PasswordHash, password.Trim());
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
            ViewBag.Error = "Correo/RUT o contraseña incorrectos, o la cuenta está desactivada.";
            return View();
        }

        // C) Crear los Claims (incluye el plan contratado por el colegio)
        var claims = await ConstruirClaimsAsync(usuario);
        var planPsicosocial = claims.Any(c => c.Type == "PlanPsicosocial" && c.Value == "true");

        var identity = new SystemClaimsIdentity(claims, "AukaAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        // D) Iniciar Sesión con Cookie
        await HttpContext.SignInAsync("AukaAuthCookie", principal);

        // E) VALIDACIÓN CLAVE PROVISORIA
        if (usuario.DebeCambiarPassword)
        {
            return RedirectToAction(nameof(CambiarPasswordObligatorio));
        }

        var premium = !claims.Any(c => c.Type == "PlanPremium" && c.Value == "false");
        return RedirigirSegunRol(usuario.Rol.ToString(), planPsicosocial, premium);
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

        var planActivo = User.FindFirst("PlanPsicosocial")?.Value == "true";
        return RedirigirSegunRol(usuario.Rol.ToString(), planActivo, User.FindFirst("PlanPremium")?.Value != "false");
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

    // 8. POST: Olvidé mi Contraseña -> envía por correo un enlace de un solo uso (dura 60 minutos)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OlvidoPassword(string emailOusuario)
    {
        if (string.IsNullOrWhiteSpace(emailOusuario))
        {
            ViewBag.Error = "Por favor ingresa tu correo de ingreso o tu RUT.";
            return View();
        }

        var dato = emailOusuario.Trim();
        var datoMin = dato.ToLower();
        // RUT escrito con o sin puntos y guion
        var rutDato = dato.Contains('@') ? "" : new string(dato.Where(ch => char.IsDigit(ch) || ch == 'k' || ch == 'K').ToArray()).ToUpperInvariant();
        var usuario = await _context.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Activo && (u.Email.ToLower() == datoMin || u.Rut == dato ||
                (rutDato.Length >= 7 && u.Rut.Replace(".", "").Replace("-", "").ToUpper() == rutDato)));

        if (usuario != null)
        {
            // Solo correos reales (el de ingreso si no es @alumnos.auka.cl, y el personal)
            var destinos = NotificacionService.Destinos(usuario);
            var ahora = DateTime.UtcNow;
            var recientes = await _context.TokensRecuperacion.CountAsync(t => t.UsuarioId == usuario.Id && t.FechaUtc > ahora.AddHours(-1));

            if (destinos.Count > 0 && recientes < 3)   // máximo 3 solicitudes por hora
            {
                // Los enlaces anteriores quedan anulados
                var previos = await _context.TokensRecuperacion.Where(t => t.UsuarioId == usuario.Id && !t.Usado && t.ExpiraUtc > ahora).ToListAsync();
                foreach (var p in previos) p.Usado = true;

                var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                _context.TokensRecuperacion.Add(new TokenRecuperacion
                {
                    UsuarioId = usuario.Id,
                    TokenHash = HashToken(token),
                    FechaUtc = ahora,
                    ExpiraUtc = ahora.AddMinutes(60)
                });
                await _context.SaveChangesAsync();

                var baseUrl = !string.IsNullOrWhiteSpace(_correoOpt.UrlBase)
                    ? _correoOpt.UrlBase.TrimEnd('/')
                    : $"{Request.Scheme}://{Request.Host}";
                var url = $"{baseUrl}/Account/RestablecerPassword?token={token}";

                var html = CorreoPlantilla.Construir(
                    "Crea una nueva contraseña",
                    $"Hola {usuario.Nombre}, recibimos una solicitud para restablecer tu contraseña de AUKA.\n\nEl enlace sirve una sola vez y vence en 60 minutos. Si no fuiste tú, ignora este correo: tu contraseña actual sigue igual.",
                    url, "");
                await _correo.EnviarAsync(destinos, "AUKA · Restablecer contraseña", html);
            }
        }

        // Mismo mensaje exista o no la cuenta (así nadie puede averiguar qué correos están registrados)
        ViewBag.Info = "Si los datos corresponden a una cuenta activa con un correo registrado, te enviamos un enlace para crear una nueva contraseña. Revisa también spam. El enlace dura 60 minutos. Si no te llega, pide a la administración de tu colegio que te restablezca la clave.";
        return View();
    }

    // 8.1 GET: enlace que llega por correo
    [HttpGet]
    public async Task<IActionResult> RestablecerPassword(string? token)
    {
        if (await BuscarTokenAsync(token) == null) { ViewBag.Invalido = true; return View(); }
        ViewBag.Token = token;
        return View();
    }

    // 8.2 POST: guarda la nueva contraseña
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestablecerPassword(string token, string nuevaPassword, string confirmarPassword)
    {
        var t = await BuscarTokenAsync(token);
        if (t == null) { ViewBag.Invalido = true; return View(); }

        ViewBag.Token = token;
        if (string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length < 6)
        {
            ViewBag.Error = "La contraseña debe tener al menos 6 caracteres.";
            return View();
        }
        if (nuevaPassword != confirmarPassword)
        {
            ViewBag.Error = "Las contraseñas no coinciden.";
            return View();
        }

        var usuario = await _context.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == t.UsuarioId && u.Activo);
        if (usuario == null) { ViewBag.Invalido = true; return View(); }

        usuario.PasswordHash = new PasswordHasher<string>().HashPassword(usuario.Email, nuevaPassword);
        usuario.DebeCambiarPassword = false;
        t.Usado = true;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "✅ Tu contraseña fue actualizada. Ya puedes iniciar sesión.";
        return RedirectToAction(nameof(Login));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<TokenRecuperacion?> BuscarTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        var h = HashToken(token);
        var ahora = DateTime.UtcNow;
        return await _context.TokensRecuperacion.FirstOrDefaultAsync(t => t.TokenHash == h && !t.Usado && t.ExpiraUtc > ahora);
    }

    // 9. GET & POST: Logout
    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            await HttpContext.SignOutAsync("AukaAuthCookie");
        }

        return RedirectToAction(nameof(Login));
    }

    // =====================================================================
    // ⚠️ SOLO PRUEBAS (borrar antes de la entrega). Solo funcionan en Development.
    // =====================================================================

    // Alterna el plan Psicosocial del colegio del usuario actual y cierra la sesión.
    // Uso: /Account/AlternarPlanPsicosocial?activo=true  (o ?activo=false; sin parámetro lo alterna)
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> AlternarPlanPsicosocial(bool? activo, [FromServices] IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) return NotFound();

        if (!int.TryParse(User.FindFirst("ColegioId")?.Value, out int colegioId))
            return RedirectToAction(nameof(Login));

        var colegio = await _context.Colegios.FindAsync(colegioId);
        if (colegio == null) return NotFound();

        // ?activo=true o ?activo=false fija el estado; sin parámetro, lo alterna
        colegio.PlanPsicosocialActivo = activo ?? !colegio.PlanPsicosocialActivo;
        await _context.SaveChangesAsync();

        await HttpContext.SignOutAsync("AukaAuthCookie");
        TempData["SuccessMessage"] = colegio.PlanPsicosocialActivo
            ? "Plan PSICOSOCIAL activado. Inicia sesión de nuevo."
            : "Plan PSICOSOCIAL desactivado. Inicia sesión de nuevo.";

        return RedirectToAction(nameof(Login));
    }

    // Cambia el plan del colegio actual para probar. Uso: /Account/CambiarPlan?plan=psicosocial  o  ?plan=premium
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> CambiarPlan(string plan, [FromServices] IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) return NotFound();
        if (!int.TryParse(User.FindFirst("ColegioId")?.Value, out int colegioId))
            return RedirectToAction(nameof(Login));

        var colegio = await _context.Colegios.FindAsync(colegioId);
        if (colegio == null) return NotFound();

        if (string.Equals(plan, "psicosocial", StringComparison.OrdinalIgnoreCase))
        { colegio.PlanPsicosocialActivo = true; colegio.SoloPsicosocial = true; }
        else if (string.Equals(plan, "premium", StringComparison.OrdinalIgnoreCase))
        { colegio.PlanPsicosocialActivo = true; colegio.SoloPsicosocial = false; }
        else return BadRequest("Usa ?plan=psicosocial o ?plan=premium");

        await _context.SaveChangesAsync();
        await HttpContext.SignOutAsync("AukaAuthCookie");
        TempData["SuccessMessage"] = $"Plan {plan.ToUpper()} aplicado. Inicia sesión de nuevo.";
        return RedirectToAction(nameof(Login));
    }

    // Crea un usuario de prueba con el rol indicado, en el colegio del usuario actual.
    // Uso: /Account/CrearUsuarioPrueba?rol=Psicologo   (o TrabajadorSocial, Orientadora, Psicopedagogo)
    // Resultado: <rol>.prueba@auka.cl (o <rol>.prueba<N° colegio>@auka.cl si ya existe en otro colegio)  /  clave Test1234
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> CrearUsuarioPrueba(string rol, [FromServices] IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) return NotFound();

        if (!Enum.TryParse<RolUsuario>(rol, true, out var rolUsuario)
            || rolUsuario == RolUsuario.SuperAdmin || rolUsuario == RolUsuario.AdminPlataforma)
        {
            return Content("Rol no válido. Usa: Psicologo, TrabajadorSocial, Orientadora, Psicopedagogo, Docente...");
        }

        if (!int.TryParse(User.FindFirst("ColegioId")?.Value, out int colegioId))
            return RedirectToAction(nameof(Login));

        // El correo debe ser único en TODOS los colegios (es el usuario de ingreso).
        // Si ya existe en otro colegio, se le agrega el N° del colegio actual.
        var baseEmail = $"{rolUsuario.ToString().ToLower()}.prueba@auka.cl";
        var email = baseEmail;
        var existente = await _context.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == baseEmail);
        if (existente != null && existente.ColegioId == colegioId)
        {
            TempData["SuccessMessage"] = $"El usuario de prueba {baseEmail} ya existe en este colegio.";
            return RedirectToAction("Index", "Home");
        }
        if (existente != null)
        {
            email = $"{rolUsuario.ToString().ToLower()}.prueba{colegioId}@auka.cl";
            if (await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email))
            {
                TempData["SuccessMessage"] = $"El usuario de prueba {email} ya existe.";
                return RedirectToAction("Index", "Home");
            }
        }

        var hasher = new PasswordHasher<string>();
        _context.Usuarios.Add(new Usuario
        {
            ColegioId = colegioId,
            Rut = $"9.{(int)rolUsuario:000}.000-1",
            Nombre = rolUsuario.ToString(),
            Apellido = "Prueba",
            Email = email,
            PasswordHash = hasher.HashPassword(email, "Test1234"),
            Rol = rolUsuario,
            Activo = true,
            DebeCambiarPassword = false
        });
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Usuario de prueba creado: {email} / Test1234";
        return RedirectToAction("Index", "Home");
    }

    // =====================================================================

    // 10. GET: Acceso Denegado
    [HttpGet]
    public IActionResult AccesoDenegado(string? returnUrl)
    {
        ViewBag.Ruta = returnUrl;
        ViewBag.Rol = User.FindFirst(SystemClaimTypes.Role)?.Value;
        return View();
    }

    // Decide a qué panel va cada rol después de autenticarse.
    // El equipo psicosocial va al panel psicosocial solo si el colegio tiene el módulo activo.
    private IActionResult RedirigirSegunRol(string rol, bool planPsicosocial = false, bool planPremium = true)
    {
        // Dueño de la plataforma: panel de colegios y planes
        if (rol == "AdminPlataforma")
        {
            return RedirectToAction("Index", "Plataforma");
        }

        // Plan Psicosocial (sin Premium): alumnos/as y apoderados/as van a su portafolio psicosocial
        if (planPsicosocial && !planPremium && (rol == "Estudiante" || rol == "Apoderado" || rol == "Docente"))
        {
            return RedirectToAction("Index", "Portafolio");
        }

        // Plan Psicosocial (sin Premium): el administrador del colegio va a su panel de administración
        if (planPsicosocial && !planPremium && rol == "SuperAdmin")
        {
            return RedirectToAction("Index", "Administracion");
        }

        if (planPsicosocial && RolesPsicosociales.Contains(rol))
        {
            return RedirectToAction("Index", "Psychosocial");
        }

        return RedirectToAction("Index", "Home");
    }

    // Construye los claims de sesión; lee el plan del colegio desde la BD
    private async Task<List<SystemClaim>> ConstruirClaimsAsync(Usuario usuario)
    {
        var colegio = await _context.Colegios
            .AsNoTracking()
            .Where(c => c.Id == usuario.ColegioId)
            .Select(c => new { c.PlanPsicosocialActivo, c.SoloPsicosocial, c.Nombre })
            .FirstOrDefaultAsync();
        var planPsicosocial = colegio?.PlanPsicosocialActivo ?? false;

        return new List<SystemClaim>
        {
            new SystemClaim(SystemClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new SystemClaim(SystemClaimTypes.Name, $"{usuario.Nombre} {usuario.Apellido}"),
            new SystemClaim(SystemClaimTypes.Email, usuario.Email),
            new SystemClaim(SystemClaimTypes.Role, usuario.Rol.ToString()),
            new SystemClaim("ColegioId", usuario.ColegioId.ToString()),
            new SystemClaim("PlanPsicosocial", planPsicosocial ? "true" : "false"),
            new SystemClaim("PlanPremium", (colegio?.SoloPsicosocial ?? false) ? "false" : "true"),
            new SystemClaim("ColegioNombre", colegio?.Nombre ?? "")
        };
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
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
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

        var email = result.Principal.FindFirst(SystemClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(email))
        {
            TempData["Error"] = "No se obtuvo la cuenta de correo desde Google.";
            return RedirectToAction("Login");
        }

        var usuario = await _context.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email && u.Activo);
        if (usuario == null)
        {
            TempData["Error"] = $"El correo {email} no se encuentra registrado en el sistema.";
            return RedirectToAction("Login");
        }

        var claims = await ConstruirClaimsAsync(usuario);
        var identity = new SystemClaimsIdentity(claims, "AukaAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("AukaAuthCookie", principal);

        var planActivo = claims.Any(c => c.Type == "PlanPsicosocial" && c.Value == "true");
        var esPremium = !claims.Any(c => c.Type == "PlanPremium" && c.Value == "false");
        return RedirigirSegunRol(usuario.Rol.ToString(), planActivo, esPremium);
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

        if (await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == emailAdmin))
        {
            ViewBag.Error = "El correo electrónico ya se encuentra registrado.";
            return View();
        }

        if (await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Rut == rutAdmin))
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
