using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

public class MiembroEquipoVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public RolUsuario Rol { get; set; }
    public string Email { get; set; } = "";
    public bool Activo { get; set; }
    public bool PendienteActivar { get; set; }      // aún no cambia su clave provisoria
    public bool UsaFichas { get; set; }             // roles que trabajan con fichas confidenciales
    public bool TieneSegundaClave { get; set; }
    public int FichasAbiertas7d { get; set; }       // solo cantidad: nunca qué alumno/a ni qué contenido
}

public class CursoCantidadVM { public string Curso { get; set; } = ""; public int Cantidad { get; set; } }

public class AdministracionVM
{
    public string Nombre { get; set; } = "";
    public string Colegio { get; set; } = "";
    public int Alumnos { get; set; }
    public int Cursos { get; set; }
    public int Equipo { get; set; }
    public int CitasSemana { get; set; }
    public int CuentasPendientes { get; set; }
    public int FichasAbiertas7d { get; set; }
    public int AnotacionesPositivas30d { get; set; }
    public int AnotacionesNegativas30d { get; set; }
    public List<MiembroEquipoVM> ListaEquipo { get; set; } = new();
    public List<CursoCantidadVM> PorCurso { get; set; } = new();

    public static string NombreRol(RolUsuario r) => r switch
    {
        RolUsuario.Psicologo => "Psicólogo/a",
        RolUsuario.Psicopedagogo => "Psicopedagogo/a",
        RolUsuario.TrabajadorSocial => "Trabajador/a Social",
        RolUsuario.Orientadora => "Orientador/a",
        RolUsuario.Docente => "Profesor/a jefe (guía)",
        _ => r.ToString()
    };
}

/// <summary>Panel del administrador de un colegio con Plan Psicosocial. Ve cantidades y estados, nunca el contenido de las fichas.</summary>
[Authorize(Policy = "AdminPsicosocial")]
public class AdministracionController : Controller
{
    private static readonly RolUsuario[] RolesEquipo =
    {
        RolUsuario.Psicologo, RolUsuario.Psicopedagogo, RolUsuario.TrabajadorSocial, RolUsuario.Orientadora, RolUsuario.Docente
    };

    private readonly ApplicationDbContext _context;

    public AdministracionController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Administracion
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // El filtro multitenant del DbContext limita todo al colegio de la sesión
        var equipoDb = await _context.Usuarios.AsNoTracking()
            .Where(u => RolesEquipo.Contains(u.Rol))
            .OrderBy(u => u.Rol).ThenBy(u => u.Apellido)
            .ToListAsync();

        var alumnos = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null)
            .Select(e => new { Nombre = e.Curso!.Nombre, Letra = e.Curso.Letra, Pendiente = e.Usuario!.DebeCambiarPassword })
            .ToListAsync();

        var desde = DateTime.UtcNow.AddDays(-7);
        var accesos = await _context.AccesosExpediente.AsNoTracking()
            .Where(a => a.FechaUtc >= desde)
            .Select(a => a.UsuarioId)
            .ToListAsync();

        // Solo totales: el libro de anotaciones no tiene colegio propio, se acota por los alumnos/as del colegio (filtro multitenant)
        var desde30 = DateTime.UtcNow.AddDays(-30);
        var tiposAnot = await _context.Anotaciones.AsNoTracking()
            .Where(a => a.Fecha >= desde30 && _context.Estudiantes.Any(e => e.Id == a.EstudianteId))
            .Select(a => a.Tipo).ToListAsync();

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        var viernes = lunes.AddDays(4);
        var citas = await _context.Citaciones.AsNoTracking()
            .CountAsync(c => c.Fecha >= lunes && c.Fecha <= viernes && c.Estado != EstadoCitacion.Cancelada);

        var vm = new AdministracionVM
        {
            Nombre = User.Identity?.Name ?? "Administrador/a",
            Colegio = User.FindFirst("ColegioNombre")?.Value ?? "",
            Alumnos = alumnos.Count,
            PorCurso = alumnos
                .GroupBy(a => string.IsNullOrEmpty(a.Letra) ? a.Nombre : a.Nombre + " " + a.Letra)
                .Select(g => new CursoCantidadVM { Curso = g.Key, Cantidad = g.Count() })
                .OrderBy(c => c.Curso).ToList(),
            CitasSemana = citas,
            AnotacionesPositivas30d = tiposAnot.Count(t => t == TipoAnotacion.Positiva),
            AnotacionesNegativas30d = tiposAnot.Count(t => t == TipoAnotacion.Negativa),
            FichasAbiertas7d = accesos.Count,
            Equipo = equipoDb.Count(u => u.Activo),
            CuentasPendientes = equipoDb.Count(u => u.Activo && u.DebeCambiarPassword) + alumnos.Count(a => a.Pendiente),
            ListaEquipo = equipoDb.Select(u => new MiembroEquipoVM
            {
                Id = u.Id,
                Nombre = $"{u.Nombre} {u.Apellido}".Trim(),
                Rol = u.Rol,
                Email = u.Email,
                Activo = u.Activo,
                PendienteActivar = u.DebeCambiarPassword,
                UsaFichas = u.Rol != RolUsuario.Docente,
                TieneSegundaClave = !string.IsNullOrEmpty(u.ClaveFichaHash),
                FichasAbiertas7d = accesos.Count(x => x == u.Id)
            }).ToList()
        };
        vm.Cursos = vm.PorCurso.Count;

        return View(vm);
    }

    // GET: /Administracion/Alumnos
    [HttpGet]
    public IActionResult Alumnos()
    {
        return View();
    }

    // ---------------------------------------------------------------
    // CURSOS Y PROFESORES/AS JEFE (guía): quién recibe las citaciones y ve las anotaciones de cada curso
    // ---------------------------------------------------------------

    private static string NombreCurso(Curso c) => string.IsNullOrEmpty(c.Letra) ? c.Nombre : $"{c.Nombre} {c.Letra}";

    // GET: /Administracion/Cursos
    [HttpGet]
    public IActionResult Cursos() => View();

    // GET: /Administracion/ListaCursos
    [HttpGet]
    public async Task<IActionResult> ListaCursos()
    {
        var anio = DateTime.Now.Year;
        // El filtro multitenant limita todo a este colegio
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.AnioAcademico == anio).ToListAsync();
        var alumnos = await _context.Estudiantes.AsNoTracking().Select(e => e.CursoId).ToListAsync();
        var docentes = await _context.Usuarios.AsNoTracking()
            .Where(u => u.Rol == RolUsuario.Docente && u.Activo).OrderBy(u => u.Nombre).ThenBy(u => u.Apellido)
            .Select(u => new { u.Id, u.Nombre, u.Apellido }).ToListAsync();

        return Json(new
        {
            cursos = cursos.OrderBy(c => c.Nombre).ThenBy(c => c.Letra).Select(c => new
            {
                id = c.Id,
                nombre = NombreCurso(c),
                alumnos = alumnos.Count(a => a == c.Id),
                jefeId = c.ProfesorJefeId
            }),
            docentes = docentes.Select(d => new { id = d.Id, nombre = $"{d.Nombre} {d.Apellido}".Trim() })
        });
    }

    // POST: /Administracion/AsignarProfesorJefe   (usuarioId = 0 quita al profesor/a jefe)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AsignarProfesorJefe(int cursoId, int usuarioId)
    {
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound(new { error = "Curso no encontrado." });

        if (usuarioId == 0)
        {
            curso.ProfesorJefeId = null;
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        var docente = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && u.Rol == RolUsuario.Docente && u.Activo);
        if (docente == null) return NotFound(new { error = "Ese profesor/a no existe en tu colegio." });

        // Un profesor/a es jefe de un solo curso
        var otro = await _context.Cursos.FirstOrDefaultAsync(c => c.ProfesorJefeId == usuarioId && c.Id != cursoId && c.AnioAcademico == curso.AnioAcademico);
        if (otro != null)
            return BadRequest(new { error = $"{docente.Nombre} {docente.Apellido} ya es profesor/a jefe de {NombreCurso(otro)}. Quítale ese curso primero." });

        curso.ProfesorJefeId = usuarioId;
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

}
