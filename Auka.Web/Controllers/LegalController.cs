using System.Security.Claims;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Auka.Web.Controllers;

/// <summary>
/// Ley 21.719 (protección de datos personales): Política de privacidad, Términos y condiciones
/// y la aceptación obligatoria al primer ingreso (se vuelve a pedir si cambia la versión).
/// </summary>
public class LegalController : Controller
{
    // Cambia la versión cuando modifiques los textos: todos deberán aceptarlos de nuevo
    public const string Version = "2026-11";
    public const string FechaVigencia = "4 de noviembre de 2026";

    // TODO: reemplazar por el correo real de contacto de privacidad de AUKA
    public const string CorreoPrivacidad = "contacto@auka.cl";

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public LegalController(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // Páginas públicas (enlazadas desde el login)
    [AllowAnonymous] public IActionResult Privacidad() => View();
    [AllowAnonymous] public IActionResult Terminos() => View();

    // GET: /Legal/Aceptar -> casilla de aceptación al primer ingreso
    [Authorize]
    [HttpGet]
    public IActionResult Aceptar(string? volver)
    {
        ViewData["Volver"] = DestinoSeguro(volver);
        return View();
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aceptar(bool acepto, string? volver)
    {
        var destino = DestinoSeguro(volver);
        if (!acepto)
        {
            ViewData["Volver"] = destino;
            ViewData["Error"] = "Para usar AUKA debes marcar la casilla de aceptación.";
            return View();
        }

        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return Forbid();
        var u = await _context.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return Forbid();

        u.AceptoTerminosUtc = DateTime.UtcNow;      // queda la fecha como respaldo del consentimiento
        u.VersionTerminosAceptada = Version;
        await _context.SaveChangesAsync();
        _cache.Set($"terminos:{uid}:{Version}", true, TimeSpan.FromHours(12));

        return LocalRedirect(destino);
    }

    // Solo rutas internas (evita redirecciones a otros sitios)
    private string DestinoSeguro(string? volver) =>
        !string.IsNullOrWhiteSpace(volver) && Url.IsLocalUrl(volver) && !volver.StartsWith("/Legal", StringComparison.OrdinalIgnoreCase) ? volver : "/";
}
