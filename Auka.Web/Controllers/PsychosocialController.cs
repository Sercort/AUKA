using System.Globalization;
using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize(Policy = "EquipoPsicosocial")]
public class PsychosocialController : Controller
{
    private static readonly HashSet<string> TiposValidos = new()
    {
        "Citación", "Taller", "Consejo", "Coordinación", "Reunión", "Institucional", "Feriado"
    };

    private readonly ApplicationDbContext _context;

    public PsychosocialController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Psychosocial
    [HttpGet]
    public IActionResult Index()
    {
        var modelo = new PsychosocialDashboardViewModel
        {
            NombreProfesional = User.Identity?.Name ?? "Profesional"
        };

        return View(modelo);
    }

    // ---------------------------------------------------------------
    // CALENDARIO COMPARTIDO (psicóloga, trabajador social, orientadora)
    // El filtro multitenant del DbContext limita todo al colegio de la sesión.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/EventosCalendario?desde=2026-10-01&hasta=2026-10-31
    [HttpGet]
    public async Task<IActionResult> EventosCalendario(string desde, string hasta)
    {
        if (!DateOnly.TryParse(desde, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
            !DateOnly.TryParse(hasta, CultureInfo.InvariantCulture, DateTimeStyles.None, out var h))
        {
            return BadRequest("Rango de fechas inválido.");
        }

        var lista = await _context.EventosCalendario
            .AsNoTracking()
            .Where(e => e.Fecha >= d && e.Fecha <= h)
            .OrderBy(e => e.Fecha).ThenBy(e => e.Hora)
            .ToListAsync();

        return Json(lista.Select(e => new
        {
            id = e.Id,
            f = e.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            h = e.Hora.HasValue ? e.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            t = e.Tipo,
            x = e.Titulo,
            r = e.Responsable,
            ok = e.Realizada
        }));
    }

    // POST: crea (id = 0) o edita una nota
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarEvento(int id, string fecha, string? hora, string tipo,
        string titulo, string responsable, bool realizada)
    {
        if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return BadRequest("Fecha inválida.");
        if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 200)
            return BadRequest("Título inválido.");
        if (!TiposValidos.Contains(tipo))
            return BadRequest("Tipo inválido.");

        TimeOnly? horaEvento = TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hh) ? hh : null;
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);

        EventoCalendario evento;
        if (id == 0)
        {
            evento = new EventoCalendario { CreadoPorId = usuarioId };
            _context.EventosCalendario.Add(evento);
        }
        else
        {
            var existente = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
            if (existente == null) return NotFound();
            evento = existente;
            evento.ModificadoPorId = usuarioId;
        }

        evento.Fecha = f;
        evento.Hora = horaEvento;
        evento.Tipo = tipo;
        evento.Titulo = titulo.Trim();
        evento.Responsable = string.IsNullOrWhiteSpace(responsable) ? "Equipo psicosocial" : responsable;
        evento.Realizada = realizada;

        await _context.SaveChangesAsync();
        return Json(new { id = evento.Id });
    }

    // POST: marca o desmarca una nota como realizada
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarEvento(int id, bool realizada)
    {
        var evento = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
        if (evento == null) return NotFound();

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        evento.Realizada = realizada;
        evento.ModificadoPorId = usuarioId;

        await _context.SaveChangesAsync();
        return Json(new { id = evento.Id });
    }

    // POST: elimina una nota (borrado lógico: el DbContext marca IsDeleted)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarEvento(int id)
    {
        var evento = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
        if (evento == null) return NotFound();

        _context.EventosCalendario.Remove(evento);
        await _context.SaveChangesAsync();
        return Json(new { id });
    }
}
