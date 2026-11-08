using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Inspectoría: registro de atrasos en la entrada, en la mañana. Después de la hora de cierre el registro del día queda
/// solo para mirar. Lo registrado llega solo a la lista de cada clase (atrasado si llegó durante la clase,
/// ausente por atraso si llegó después) y a la ficha del alumno/a. Inspectoría no ve ni corrige la asistencia de las clases.
/// </summary>
[Authorize(Roles = "Inspector,UTP")]
public class InspectoriaController : Controller
{
    private readonly ApplicationDbContext _context;
    public InspectoriaController(ApplicationDbContext context) => _context = context;

    /// <summary>Hasta esta hora se registran atrasos de llegada (después el registro de la mañana se cierra).</summary>
    public static readonly TimeSpan HoraCierre = new(13, 0, 0);
    public static readonly string[] Motivos = { "Locomoción / tráfico", "Se quedó dormido/a", "Hora médica", "Trámite familiar", "Sin justificar" };

    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }
    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }

    public sealed record FilaAtraso(int Id, int EstudianteId, string Nombre, string Curso, TimeSpan Hora, string Motivo, bool Justificado, int DiasMes);

    [HttpGet]
    public async Task<IActionResult> Atrasos(DateOnly? fecha)
    {
        var colegioId = ColegioId();
        var dia = fecha ?? AsistenciaController.Hoy;
        var f = AsistenciaController.DiaUtc(dia);
        var hoy = await _context.AtrasosLlegada.AsNoTracking().Include(a => a.Estudiante).ThenInclude(e => e!.Usuario).Include(a => a.Estudiante).ThenInclude(e => e!.Curso)
            .Where(a => a.Fecha == f).OrderByDescending(a => a.Hora).ToListAsync();
        var inicioMes = AsistenciaController.DiaUtc(new DateOnly(dia.Year, dia.Month, 1));
        var ids = hoy.Select(a => a.EstudianteId).ToList();
        var mes = (await _context.AtrasosLlegada.AsNoTracking().Where(a => ids.Contains(a.EstudianteId) && a.Fecha >= inicioMes).Select(a => new { a.EstudianteId, a.Fecha }).ToListAsync())
            .GroupBy(x => x.EstudianteId).ToDictionary(g => g.Key, g => g.Select(x => x.Fecha).Distinct().Count());
        ViewBag.Fecha = dia;
        ViewBag.Abierto = dia == AsistenciaController.Hoy && DateTime.Now.TimeOfDay < HoraCierre;
        ViewBag.Alumnos = await _context.Estudiantes.AsNoTracking()
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null && e.Curso.AnioAcademico == dia.Year)
            .OrderBy(e => e.Usuario!.Apellido).Select(e => new OpcionVm(e.Id, e.Usuario!.Apellido + ", " + e.Usuario.Nombre + " · " + e.Curso!.Nombre + " " + e.Curso.Letra + " · " + e.Usuario.Rut))
            .ToListAsync();
        return View(hoy.Select(a => new FilaAtraso(a.Id, a.EstudianteId, $"{a.Estudiante?.Usuario?.Apellido}, {a.Estudiante?.Usuario?.Nombre}",
            $"{a.Estudiante?.Curso?.Nombre} {a.Estudiante?.Curso?.Letra}".Trim(), a.Hora, a.Motivo, a.Justificado, mes.GetValueOrDefault(a.EstudianteId))).ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar(string? alumno, TimeSpan? hora, string? motivo, bool justificado = false)
    {
        if (DateTime.Now.TimeOfDay >= HoraCierre && !User.IsInRole("UTP"))
        {
            TempData["Error"] = $"El registro de atrasos de la mañana se cerró a las {HoraCierre:hh\\:mm}.";
            return RedirectToAction(nameof(Atrasos));
        }
        // El alumno/a viene como "Apellido, Nombre · Curso · RUT" (lista de sugerencias) o solo su RUT
        var rutTxt = (alumno ?? "").Split('·').Last().Trim();
        if (!Rut.TryValidar(rutTxt, out var rut, out _)) { TempData["Error"] = "Elige al alumno/a de la lista (o escribe su RUT)."; return RedirectToAction(nameof(Atrasos)); }
        var est = (await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null && e.Curso.AnioAcademico == DateTime.Now.Year).ToListAsync())
            .FirstOrDefault(e => Rut.Normalizar(e.Usuario!.Rut) == rut);
        if (est == null) { TempData["Error"] = "No hay alumno/a con ese RUT este año."; return RedirectToAction(nameof(Atrasos)); }
        var dia = AsistenciaController.DiaUtc(AsistenciaController.Hoy);
        if (await _context.AtrasosLlegada.AnyAsync(a => a.EstudianteId == est.Id && a.Fecha == dia))
        {
            TempData["Error"] = $"{est.Usuario!.Nombre} {est.Usuario.Apellido} ya tiene su atraso registrado hoy.";
            return RedirectToAction(nameof(Atrasos));
        }
        motivo = string.IsNullOrWhiteSpace(motivo) ? "Sin justificar" : motivo.Trim()[..Math.Min(motivo.Trim().Length, 200)];
        var h = hora ?? new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
        _context.AtrasosLlegada.Add(new AtrasoLlegada { ColegioId = ColegioId(), EstudianteId = est.Id, Fecha = dia, Hora = h, Motivo = motivo, Justificado = justificado, RegistradoPorId = UsuarioId() });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"🚪 Atraso de {est.Usuario!.Nombre} {est.Usuario.Apellido} ({est.Curso!.Nombre} {est.Curso.Letra}) registrado a las {h:hh\\:mm}. Ya aparece en la lista de su clase.";
        return RedirectToAction(nameof(Atrasos));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quitar(int id)
    {
        var a = await _context.AtrasosLlegada.FirstOrDefaultAsync(x => x.Id == id);
        if (a != null && (a.Fecha == AsistenciaController.DiaUtc(AsistenciaController.Hoy) || User.IsInRole("UTP")))
        {
            _context.AtrasosLlegada.Remove(a);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Registro quitado.";
        }
        return RedirectToAction(nameof(Atrasos));
    }
}
