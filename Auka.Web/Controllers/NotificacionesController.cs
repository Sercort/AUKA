using System.Security.Claims;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>Campana de avisos dentro de AUKA (el correo es el canal principal; esto es para quien tiene la app abierta).</summary>
[Authorize]
public class NotificacionesController : Controller
{
    private readonly ApplicationDbContext _context;
    public NotificacionesController(ApplicationDbContext context) => _context = context;

    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    // GET: /Notificaciones/Lista
    [HttpGet]
    public async Task<IActionResult> Lista()
    {
        var uid = UsuarioActualId();
        var items = await _context.Notificaciones.AsNoTracking()
            .Where(n => n.UsuarioId == uid)
            .OrderByDescending(n => n.CreadaEnUtc).Take(15)
            .Select(n => new { n.Id, n.Titulo, n.Mensaje, n.Enlace, n.Leida, n.CreadaEnUtc })
            .ToListAsync();
        var sinLeer = await _context.Notificaciones.CountAsync(n => n.UsuarioId == uid && !n.Leida);
        return Json(new
        {
            sinLeer,
            items = items.Select(n => new
            {
                id = n.Id,
                t = n.Titulo,
                m = n.Mensaje,
                e = n.Enlace,
                l = n.Leida,
                f = DateTime.SpecifyKind(n.CreadaEnUtc, DateTimeKind.Utc).ToString("o")
            })
        });
    }

    // POST: /Notificaciones/MarcarLeidas
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarLeidas()
    {
        var uid = UsuarioActualId();
        var pend = await _context.Notificaciones.Where(n => n.UsuarioId == uid && !n.Leida).ToListAsync();
        foreach (var n in pend) n.Leida = true;
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }
}
