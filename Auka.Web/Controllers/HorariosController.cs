using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Horarios (Plan Premium):
///  - Jornada del colegio: bloques, recreos, almuerzo y salida del viernes (configurable).
///  - Horario de cada curso: grilla lunes a viernes; lo arma quien tenga el permiso "Armar horarios".
///  - Horario de cada profesor/a: se arma solo desde los cursos. Personal agrupado por departamento.
/// </summary>
[Authorize]
public class HorariosController : Controller
{
    private readonly ApplicationDbContext _context;
    public HorariosController(ApplicationDbContext context) => _context = context;

    public static readonly string[] Dias = { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes" };

    public static readonly string[] AsignaturasBase =
    {
        "Lenguaje y Comunicación", "Matemática", "Historia, Geografía y Cs. Sociales", "Ciencias Naturales", "Biología", "Química", "Física",
        "Inglés", "Educación Física y Salud", "Artes Visuales", "Música", "Tecnología", "Orientación", "Religión", "Filosofía",
        "Educación Ciudadana", "Consejo de Curso", "Especialidad TP", "Taller"
    };

    public static readonly string[] Departamentos =
    {
        "Lenguaje", "Matemática", "Historia", "Ciencias", "Inglés", "Educación Física", "Artes y Música", "Tecnología",
        "Religión y Filosofía", "Educación Diferencial (PIE)", "Educación Parvularia", "Educación Básica", "Especialidad TP"
    };

    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }
    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }
    private Task<bool> PuedeEditarAsync() => Permisos.PuedeAsync(_context, User, Permisos.Horarios);

    // ---------------------------------------------------------------- Jornada
    /// <summary>Bloques del colegio. Si aún no tiene, crea la jornada típica (8:00, bloques de 45 min).</summary>
    private async Task<List<BloqueJornada>> JornadaAsync(int colegioId)
    {
        var bloques = await _context.BloquesJornada.Where(b => b.ColegioId == colegioId).OrderBy(b => b.Orden).ToListAsync();
        if (bloques.Count > 0) return bloques;

        var t = new TimeSpan(8, 0, 0);
        var plan = new (TipoBloque Tipo, int Min)[]
        {
            (TipoBloque.Clase, 45), (TipoBloque.Clase, 45), (TipoBloque.Recreo, 15),
            (TipoBloque.Clase, 45), (TipoBloque.Clase, 45), (TipoBloque.Recreo, 15),
            (TipoBloque.Clase, 45), (TipoBloque.Clase, 45), (TipoBloque.Almuerzo, 45),
            (TipoBloque.Clase, 45), (TipoBloque.Clase, 45)
        };
        var orden = 1;
        foreach (var (tipo, min) in plan)
        {
            bloques.Add(new BloqueJornada { ColegioId = colegioId, Orden = orden++, HoraInicio = t, HoraFin = t.Add(TimeSpan.FromMinutes(min)), Tipo = tipo });
            t = t.Add(TimeSpan.FromMinutes(min));
        }
        _context.BloquesJornada.AddRange(bloques);
        await _context.SaveChangesAsync();
        return bloques;
    }

    private async Task<(TimeSpan Salida, bool Tarde)> ViernesAsync(int colegioId)
    {
        var c = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == colegioId)
            .Select(x => new { x.ViernesSalida, x.ViernesTardeActiva }).FirstOrDefaultAsync();
        return (c?.ViernesSalida ?? new TimeSpan(13, 0, 0), c?.ViernesTardeActiva ?? false);
    }

    [HttpGet]
    public async Task<IActionResult> Jornada()
    {
        var colegioId = ColegioId();
        ViewBag.PuedeEditar = await PuedeEditarAsync();
        var (salida, tarde) = await ViernesAsync(colegioId);
        ViewBag.ViernesSalida = salida;
        ViewBag.ViernesTarde = tarde;
        ViewBag.ConClases = await _context.ClasesHorario.CountAsync(c => c.ColegioId == colegioId);
        return View(await JornadaAsync(colegioId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarJornada(string[] inicio, string[] fin, int[] tipo, string? viernesSalida, bool viernesTarde)
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();

        var nuevos = new List<(TimeSpan I, TimeSpan F, TipoBloque T)>();
        for (var i = 0; i < Math.Min(inicio.Length, Math.Min(fin.Length, tipo.Length)); i++)
        {
            if (!TimeSpan.TryParse(inicio[i], out var a) || !TimeSpan.TryParse(fin[i], out var b) || b <= a) continue;
            nuevos.Add((a, b, Enum.IsDefined(typeof(TipoBloque), tipo[i]) ? (TipoBloque)tipo[i] : TipoBloque.Clase));
        }
        nuevos = nuevos.OrderBy(n => n.I).ToList();
        if (nuevos.Count == 0 || nuevos.Count > 20)
        {
            TempData["Error"] = "Revisa los bloques: cada uno necesita hora de inicio y de término (máximo 20).";
            return RedirectToAction(nameof(Jornada));
        }
        for (var i = 1; i < nuevos.Count; i++)
            if (nuevos[i].I < nuevos[i - 1].F)
            {
                TempData["Error"] = $"Hay bloques que se topan ({nuevos[i - 1].I:hh\\:mm}–{nuevos[i - 1].F:hh\\:mm} y {nuevos[i].I:hh\\:mm}–{nuevos[i].F:hh\\:mm}).";
                return RedirectToAction(nameof(Jornada));
            }

        // Se actualizan por orden: las clases ya asignadas se mantienen en su bloque; los bloques que sobran se eliminan
        var actuales = await _context.BloquesJornada.Where(b => b.ColegioId == colegioId).OrderBy(b => b.Orden).ToListAsync();
        foreach (var b in actuales) b.Orden += 1000;   // evita choque del índice único mientras se reordena
        await _context.SaveChangesAsync();
        for (var i = 0; i < nuevos.Count; i++)
        {
            var b = i < actuales.Count ? actuales[i] : null;
            if (b == null) { b = new BloqueJornada { ColegioId = colegioId }; _context.BloquesJornada.Add(b); }
            b.Orden = i + 1; b.HoraInicio = nuevos[i].I; b.HoraFin = nuevos[i].F; b.Tipo = nuevos[i].T;
        }
        if (actuales.Count > nuevos.Count) _context.BloquesJornada.RemoveRange(actuales.Skip(nuevos.Count));
        // Recreos y almuerzo no llevan clases
        var idsNoClase = actuales.Take(nuevos.Count).Where(b => b.Tipo != TipoBloque.Clase).Select(b => b.Id).ToList();
        _context.ClasesHorario.RemoveRange(await _context.ClasesHorario.Where(c => idsNoClase.Contains(c.BloqueJornadaId)).ToListAsync());

        var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        if (col != null)
        {
            col.ViernesSalida = TimeSpan.TryParse(viernesSalida, out var vs) ? vs : new TimeSpan(13, 0, 0);
            col.ViernesTardeActiva = viernesTarde;
        }
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Jornada guardada. Los horarios ya usan los bloques nuevos.";
        return RedirectToAction(nameof(Jornada));
    }

    // ---------------------------------------------------------------- Listado de cursos
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var usuarioId = UsuarioId();
        if (User.IsInRole("Estudiante") || User.IsInRole("Apoderado")) return RedirectToAction(nameof(MiHorario));

        var anio = DateTime.Now.Year;
        var todos = await _context.Cursos.Include(c => c.ProfesorJefe).Include(c => c.Estudiantes)
            .Where(c => c.Nombre != "Pacientes").ToListAsync();
        if (todos.Any(c => c.AnioAcademico == anio)) todos = todos.Where(c => c.AnioAcademico == anio).ToList();
        ViewBag.PuedeEditar = await PuedeEditarAsync();
        return View(todos.OrderBy(c => c.Nombre).ThenBy(c => c.Letra).ToList());
    }

    // ---------------------------------------------------------------- Horario del curso
    [HttpGet]
    [Route("Horarios/Curso/{id}")]
    [Route("Horarios/VerHorarioCurso/{id}")]
    public async Task<IActionResult> VerHorarioCurso(int id)
    {
        var colegioId = ColegioId();
        var curso = await _context.Cursos.Include(c => c.ProfesorJefe).FirstOrDefaultAsync(c => c.Id == id);
        if (curso == null) return NotFound();

        var clases = await _context.ClasesHorario.AsNoTracking().Include(c => c.Profesor).Where(c => c.CursoId == id).ToListAsync();
        var vm = await ArmarGrillaAsync(colegioId, clases, porProfesor: false);
        vm.Titulo = $"{curso.Nombre} {curso.Letra}".Trim();
        vm.Subtitulo = curso.ProfesorJefe != null ? $"Profesor/a jefe: {curso.ProfesorJefe.Nombre} {curso.ProfesorJefe.Apellido}" : "Horario semanal del curso";
        vm.CursoId = id;
        vm.Editable = await PuedeEditarAsync() && curso.AnioAcademico >= DateTime.Now.Year;
        if (vm.Editable)
        {
            vm.Profesores = await _context.Usuarios.AsNoTracking()
                .Where(u => u.ColegioId == colegioId && u.Activo && (u.Rol == RolUsuario.Docente || u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Director || u.Rol == RolUsuario.Orientadora))
                .OrderBy(u => u.Apellido).Select(u => new OpcionVm(u.Id, u.Nombre + " " + u.Apellido + (u.Departamento != null ? " · " + u.Departamento : ""))).ToListAsync();
            var usadas = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId).Select(c => c.Asignatura).Distinct().ToListAsync();
            vm.Asignaturas = AsignaturasBase.Union(usadas).OrderBy(a => a).ToList();
        }
        return View("Curso", vm);
    }

    /// <summary>Guarda (o borra, si la asignatura viene vacía) la clase de un bloque. No deja a un profesor/a en dos cursos a la vez.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarClase(int cursoId, int dia, int bloqueId, string? asignatura, int? profesorId, string? sala)
    {
        if (!await PuedeEditarAsync()) return Json(new { ok = false, error = "No tienes permiso para modificar horarios." });
        var colegioId = ColegioId();
        if (dia < 1 || dia > 5) return Json(new { ok = false, error = "Día no válido." });
        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId);
        var bloque = await _context.BloquesJornada.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bloqueId && b.ColegioId == colegioId);
        if (curso == null || bloque == null) return Json(new { ok = false, error = "Curso o bloque no encontrado." });
        if (bloque.Tipo != TipoBloque.Clase) return Json(new { ok = false, error = "Ese bloque es recreo o almuerzo." });
        var (salida, tarde) = await ViernesAsync(colegioId);
        if (dia == 5 && !tarde && bloque.HoraInicio >= salida) return Json(new { ok = false, error = $"El viernes la salida es a las {salida:hh\\:mm}. Activa la tarde del viernes en «Jornada» para recuperar clases." });

        var clase = await _context.ClasesHorario.FirstOrDefaultAsync(c => c.CursoId == cursoId && c.Dia == dia && c.BloqueJornadaId == bloqueId);
        asignatura = (asignatura ?? "").Trim();
        if (asignatura.Length == 0)
        {
            if (clase != null) { _context.ClasesHorario.Remove(clase); await _context.SaveChangesAsync(); }
            return Json(new { ok = true });
        }
        if (asignatura.Length > 80) asignatura = asignatura[..80];

        if (profesorId is > 0)
        {
            var prof = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == profesorId && u.ColegioId == colegioId && u.Activo);
            if (prof == null) return Json(new { ok = false, error = "Profesor/a no válido." });
            var choque = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
                .FirstOrDefaultAsync(c => c.ProfesorId == profesorId && c.Dia == dia && c.BloqueJornadaId == bloqueId && c.CursoId != cursoId);
            if (choque != null)
                return Json(new { ok = false, error = $"{prof.Nombre} {prof.Apellido} ya tiene clase en {choque.Curso?.Nombre} {choque.Curso?.Letra} ese {Dias[dia - 1].ToLower()} a las {bloque.HoraInicio:hh\\:mm}." });
        }
        else profesorId = null;

        if (clase == null)
        {
            clase = new ClaseHorario { ColegioId = colegioId, CursoId = cursoId, Dia = dia, BloqueJornadaId = bloqueId };
            _context.ClasesHorario.Add(clase);
        }
        clase.Asignatura = asignatura;
        clase.ProfesorId = profesorId;
        clase.Sala = string.IsNullOrWhiteSpace(sala) ? null : sala.Trim()[..Math.Min(sala.Trim().Length, 40)];
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // ---------------------------------------------------------------- Horarios del personal
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Inspector,Orientadora")]
    public async Task<IActionResult> Profesores()
    {
        var colegioId = ColegioId();
        var personal = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && (u.Rol == RolUsuario.Docente || u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Director
                || u.Rol == RolUsuario.Inspector || u.Rol == RolUsuario.Orientadora || u.Rol == RolUsuario.SuperAdmin))
            .OrderBy(u => u.Apellido).ToListAsync();
        var horas = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorId != null)
            .GroupBy(c => c.ProfesorId!.Value).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        ViewBag.Horas = horas.ToDictionary(h => h.Key, h => h.N);
        ViewBag.PuedeEditar = await PuedeEditarAsync();
        return View(personal);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AsignarDepartamento(int usuarioId, string? departamento)
    {
        if (!await PuedeEditarAsync()) return Json(new { ok = false, error = "No tienes permiso." });
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.ColegioId == ColegioId());
        if (u == null) return Json(new { ok = false, error = "Funcionario/a no encontrado." });
        departamento = (departamento ?? "").Trim();
        u.Departamento = departamento.Length == 0 ? null : departamento[..Math.Min(departamento.Length, 60)];
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Inspector,Orientadora")]
    public async Task<IActionResult> Profesor(int id)
    {
        var colegioId = ColegioId();
        var u = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ColegioId == colegioId);
        if (u == null) return NotFound();
        var clases = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso).Where(c => c.ProfesorId == id).ToListAsync();
        var vm = await ArmarGrillaAsync(colegioId, clases, porProfesor: true);
        vm.Titulo = $"{u.Nombre} {u.Apellido}";
        vm.Subtitulo = (u.Departamento ?? NombreRol(u.Rol)) + $" · {clases.Count} {(clases.Count == 1 ? "bloque" : "bloques")} de clase a la semana";
        return View("Profesor", vm);
    }

    /// <summary>Atajo: el docente ve su horario; el alumno/a (o su apoderado/a) el de su curso.</summary>
    [HttpGet]
    public async Task<IActionResult> MiHorario()
    {
        var uid = UsuarioId();
        if (User.IsInRole("Estudiante"))
        {
            var cursoId = await _context.Estudiantes.Where(e => e.UsuarioId == uid).Select(e => (int?)e.CursoId).FirstOrDefaultAsync();
            if (cursoId != null) return Redirect($"/Horarios/Curso/{cursoId}");
        }
        if (User.IsInRole("Apoderado"))
        {
            var cursoId = await _context.EstudiantesApoderados.Where(x => x.Apoderado != null && x.Apoderado.UsuarioId == uid && x.Estudiante != null)
                .OrderByDescending(x => x.EsSostenedorPrincipal).Select(x => (int?)x.Estudiante!.CursoId).FirstOrDefaultAsync();
            if (cursoId != null) return Redirect($"/Horarios/Curso/{cursoId}");
        }
        if (User.IsInRole("Docente") || User.IsInRole("UTP") || User.IsInRole("Director")) return RedirectToAction(nameof(Profesor), new { id = uid });
        return RedirectToAction(nameof(Index));
    }

    // Enlace antiguo
    [HttpGet]
    public IActionResult HorarioPersonal() => RedirectToAction(nameof(Profesores));

    public static string NombreRol(RolUsuario r) => r switch
    {
        RolUsuario.SuperAdmin => "Administración", RolUsuario.Director => "Dirección", RolUsuario.UTP => "Unidad Técnico-Pedagógica",
        RolUsuario.Inspector => "Inspectoría", RolUsuario.Orientadora => "Orientación", RolUsuario.Docente => "Docente", _ => r.ToString()
    };

    // ---------------------------------------------------------------- Grilla
    private async Task<GrillaVm> ArmarGrillaAsync(int colegioId, List<ClaseHorario> clases, bool porProfesor)
    {
        var (salida, tarde) = await ViernesAsync(colegioId);
        var vm = new GrillaVm
        {
            Bloques = await JornadaAsync(colegioId),
            ViernesSalida = salida,
            ViernesTarde = tarde,
            PorProfesor = porProfesor
        };
        foreach (var c in clases)
            vm.Celdas[(c.Dia, c.BloqueJornadaId)] = new CeldaVm(c.Asignatura,
                porProfesor ? $"{c.Curso?.Nombre} {c.Curso?.Letra}".Trim() : (c.Profesor != null ? $"{c.Profesor.Nombre} {c.Profesor.Apellido}" : null),
                c.Sala, c.ProfesorId);
        return vm;
    }
}

public record CeldaVm(string Asignatura, string? Detalle, string? Sala, int? ProfesorId);

public class GrillaVm
{
    public string Titulo { get; set; } = "";
    public string Subtitulo { get; set; } = "";
    public int? CursoId { get; set; }
    public bool Editable { get; set; }
    public bool PorProfesor { get; set; }
    public List<BloqueJornada> Bloques { get; set; } = new();
    public Dictionary<(int Dia, int BloqueId), CeldaVm> Celdas { get; set; } = new();
    public TimeSpan ViernesSalida { get; set; } = new(13, 0, 0);
    public bool ViernesTarde { get; set; }
    public List<OpcionVm> Profesores { get; set; } = new();
    public List<string> Asignaturas { get; set; } = new();
}
