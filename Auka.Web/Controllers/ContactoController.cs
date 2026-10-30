using System.Text.RegularExpressions;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Auka.Web.Controllers;

/// <summary>Formulario público: lo que escriben colegios interesados o clientes llega al panel del dueño de la plataforma.</summary>
[AllowAnonymous]
public class ContactoController : Controller
{
    private static readonly string[] Tipos = { "Quiero contratar AUKA", "Quiero una demo", "Consulta", "Sugerencia", "Reclamo" };
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    public ContactoController(ApplicationDbContext context, IMemoryCache cache) { _context = context; _cache = cache; }

    [HttpGet]
    public IActionResult Index() { ViewBag.Tipos = Tipos; return View(); }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(string tipo, string nombre, string? cargo, string? colegio, string correo, string? telefono, string mensaje, string? web)
    {
        ViewBag.Tipos = Tipos;
        // Campo trampa para robots: las personas no lo ven ni lo llenan
        if (!string.IsNullOrWhiteSpace(web)) return RedirectToAction(nameof(Gracias));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var llave = "contacto:" + ip;
        var n = _cache.GetOrCreate(llave, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new int[1]; })!;
        if (n[0] >= 5) { ViewBag.Error = "Enviaste varios mensajes seguidos. Inténtalo de nuevo en un rato."; return View(); }

        nombre = (nombre ?? "").Trim(); correo = (correo ?? "").Trim(); mensaje = (mensaje ?? "").Trim();
        if (nombre.Length < 2) { ViewBag.Error = "Escribe tu nombre."; return View(); }
        if (!Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) { ViewBag.Error = "Escribe un correo válido para poder responderte."; return View(); }
        if (mensaje.Length < 5) { ViewBag.Error = "Cuéntanos tu mensaje."; return View(); }
        if (!Tipos.Contains(tipo)) tipo = "Consulta";

        string Corta(string? t, int max) { t = (t ?? "").Trim(); return t.Length > max ? t[..max] : t; }
        _context.MensajesPlataforma.Add(new MensajePlataforma
        {
            Tipo = tipo,
            Nombre = Corta(nombre, 120),
            Cargo = Corta(cargo, 80),
            ColegioNombre = Corta(colegio, 140),
            Correo = Corta(correo, 160).ToLowerInvariant(),
            Telefono = Corta(telefono, 40),
            Mensaje = Corta(mensaje, 2000),
            Estado = EstadoMensaje.Nuevo,
            FechaUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        n[0]++;
        return RedirectToAction(nameof(Gracias));
    }

    [HttpGet]
    public IActionResult Gracias() => View();
}
