using System.Globalization;
using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

public class PortafolioAlumnoVm
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Curso { get; set; } = "";
}

public class PortafolioCitacionVm
{
    public int Id { get; set; }
    public string Fecha { get; set; } = "";
    public string Hora { get; set; } = "";
    public string CitadoA { get; set; } = "";
    public string Motivo { get; set; } = "";
    public EstadoCitacion Estado { get; set; }
    public bool PuedeConfirmar { get; set; }
}

public class PortafolioAnotacionVm
{
    public string Fecha { get; set; } = "";
    public bool Positiva { get; set; }
    public string Detalle { get; set; } = "";
}

public class PortafolioVm
{
    public bool EsApoderado { get; set; }
    public bool EsDocente { get; set; }
    public List<PortafolioAlumnoVm> Alumnos { get; set; } = new();
    public PortafolioAlumnoVm? Actual { get; set; }
    public List<PortafolioCitacionVm> Citaciones { get; set; } = new();
    public List<PortafolioAnotacionVm> Anotaciones { get; set; } = new();
    public int Positivas { get; set; }
    public int Negativas { get; set; }
}

/// <summary>
/// Portafolio psicosocial de alumnos/as y apoderados/as: solo sus citaciones y sus anotaciones de conducta.
/// No muestra cursos, notas, clubes ni horario (eso es del Plan Premium).
/// </summary>
[Authorize(Policy = "FamiliaPsicosocial")]
public class PortafolioController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly INotificacionService _notif;

    public PortafolioController(ApplicationDbContext context, INotificacionService notif)
    {
        _context = context;
        _notif = notif;
    }

    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    private bool EsApoderado => User.FindFirst(ClaimTypes.Role)?.Value == "Apoderado";
    private bool EsDocente => User.FindFirst(ClaimTypes.Role)?.Value == "Docente";

    private async Task<List<PortafolioAlumnoVm>> MisAlumnosAsync()
    {
        var uid = UsuarioActualId();
        List<Estudiante> ests;
        if (EsDocente)
        {
            // Profesor/a jefe: los alumnos/as de los cursos que tiene a su cargo
            ests = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.Curso != null && e.Curso.ProfesorJefeId == uid)
                .ToListAsync();
        }
        else if (EsApoderado)
        {
            // Estudiantes tiene filtro por colegio: nunca aparece un alumno/a de otro colegio
            ests = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.Apoderados.Any(x => x.Apoderado != null && x.Apoderado.UsuarioId == uid))
                .ToListAsync();
        }
        else
        {
            ests = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.UsuarioId == uid).ToListAsync();
        }
        return ests.Where(e => e.Usuario != null).GroupBy(e => e.Id).Select(g => g.First())
            .OrderBy(e => e.Usuario!.Nombre)
            .Select(e => new PortafolioAlumnoVm
            {
                Id = e.Id,
                Nombre = $"{e.Usuario!.Nombre} {e.Usuario.Apellido}".Trim(),
                Curso = e.Curso == null ? "" : (string.IsNullOrEmpty(e.Curso.Letra) ? e.Curso.Nombre : $"{e.Curso.Nombre} {e.Curso.Letra}")
            }).ToList();
    }

    // GET: /Portafolio
    [HttpGet]
    public async Task<IActionResult> Index(int? estudianteId)
    {
        var vm = new PortafolioVm { EsApoderado = EsApoderado, EsDocente = EsDocente, Alumnos = await MisAlumnosAsync() };
        vm.Actual = vm.Alumnos.FirstOrDefault(a => a.Id == estudianteId) ?? vm.Alumnos.FirstOrDefault();
        if (vm.Actual == null) return View(vm);

        var citas = await _context.Citaciones.AsNoTracking()
            .Where(c => c.EstudianteId == vm.Actual.Id && c.Estado != EstadoCitacion.Cancelada)
            .OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Hora).Take(30).ToListAsync();

        vm.Citaciones = citas
            .Where(c => EsDocente ? c.CitadoA == "Profesor jefe"
                      : EsApoderado ? c.CitadoA == "Apoderado" || c.CitadoA == "Alumno + apoderado"
                                    : c.CitadoA == "Alumno" || c.CitadoA == "Alumno + apoderado")
            .Select(c => new PortafolioCitacionVm
            {
                Id = c.Id,
                Fecha = c.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                Hora = c.Hora.HasValue ? c.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
                CitadoA = c.CitadoA,
                Motivo = c.Motivo,
                Estado = c.Estado,
                PuedeConfirmar = c.Estado == EstadoCitacion.Pendiente
            }).ToList();

        // Solo anotaciones de conducta del propio alumno/a (las clínicas del equipo nunca se muestran aquí)
        var anots = await _context.Anotaciones.AsNoTracking()
            .Where(a => a.EstudianteId == vm.Actual.Id)
            .OrderByDescending(a => a.Fecha).Take(30).ToListAsync();
        vm.Anotaciones = anots.Select(a => new PortafolioAnotacionVm
        {
            Fecha = a.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Positiva = (int)a.Tipo == 1,
            Detalle = a.Detalle
        }).ToList();
        vm.Positivas = vm.Anotaciones.Count(a => a.Positiva);
        vm.Negativas = vm.Anotaciones.Count(a => !a.Positiva);
        return View(vm);
    }

    // POST: /Portafolio/Confirmar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(int id)
    {
        var cit = await _context.Citaciones.Include(c => c.Estudiante).ThenInclude(e => e!.Usuario)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (cit == null) return NotFound();

        var mios = (await MisAlumnosAsync()).Any(a => a.Id == cit.EstudianteId);
        if (!mios) return Forbid();
        // Solo confirma quien fue citado/a
        bool puede = EsDocente ? cit.CitadoA == "Profesor jefe"
                   : EsApoderado ? cit.CitadoA is "Apoderado" or "Alumno + apoderado"
                                 : cit.CitadoA is "Alumno" or "Alumno + apoderado";
        if (!puede) return Forbid();

        if (cit.Estado == EstadoCitacion.Pendiente)
        {
            cit.Estado = EstadoCitacion.Confirmada;
            cit.ConfirmadaEnUtc = DateTime.UtcNow;
            cit.ConfirmadaPorId = UsuarioActualId();
            await _context.SaveChangesAsync();

            var quien = User.Identity?.Name ?? "Un/a usuario/a";
            var nombre = cit.Estudiante?.Usuario == null ? "el alumno/a" : $"{cit.Estudiante.Usuario.Nombre} {cit.Estudiante.Usuario.Apellido}".Trim();
            await _notif.NotificarAsync(new[] { cit.CreadaPorId }, "Citación confirmada",
                $"{quien} confirmó su asistencia a la citación del {cit.Fecha:dd-MM-yyyy} ({nombre}).", "/Psychosocial");
        }
        return RedirectToAction(nameof(Index), new { estudianteId = cit.EstudianteId });
    }
}
