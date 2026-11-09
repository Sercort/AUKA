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
        "Educación Ciudadana", "Consejo de Curso", "Orientación y Consejo de Curso", "Especialidad TP", "Taller"
    };

    public static readonly string[] Departamentos =
    {
        "Lenguaje", "Matemática", "Historia", "Ciencias", "Inglés", "Educación Física", "Artes y Música", "Tecnología",
        "Religión y Filosofía", "Educación Diferencial (PIE)", "Educación Parvularia", "Educación Básica", "Especialidad TP"
    };

    // Asignaturas que hace el profesor/a jefe con su curso
    public static bool EsDeJefatura(string? a)
    {
        var x = (a ?? "").Trim().ToLowerInvariant();
        return x.StartsWith("consejo") || x.StartsWith("orientaci") || x.StartsWith("jefatura");
    }

    // Asignatura → departamento (para sugerir reemplazos de la misma área)
    public static string? DeptoDe(string? a)
    {
        var x = (a ?? "").ToLowerInvariant();
        if (x.Contains("lenguaje") || x.Contains("lectura") || x.Contains("comunicaci")) return "Lenguaje";
        if (x.Contains("matem")) return "Matemática";
        if (x.Contains("historia") || x.Contains("ciudadan") || x.Contains("sociales")) return "Historia";
        if (x.Contains("ciencia") || x.Contains("biolog") || x.Contains("químic") || x.Contains("quimic") || x.Contains("físic") && !x.Contains("educaci")) return "Ciencias";
        if (x.Contains("ingl")) return "Inglés";
        if (x.Contains("educación física") || x.Contains("educacion fisica") || x.Contains("deporte")) return "Educación Física";
        if (x.Contains("arte") || x.Contains("músic") || x.Contains("music")) return "Artes y Música";
        if (x.Contains("tecnolog")) return "Tecnología";
        if (x.Contains("religi") || x.Contains("filosof")) return "Religión y Filosofía";
        if (x.Contains("especialidad")) return "Especialidad TP";
        return null;
    }

    /// <summary>
    /// Departamento a partir de las asignaturas escritas en Personal ("Lenguaje y Comunicación, Historia" → "Lenguaje").
    /// Si no se reconoce, se usa la primera asignatura tal cual. Una sola fuente: lo que se escribe en Personal.
    /// </summary>
    public static string? DepartamentoDesde(string? asignaturas, string? actual)
    {
        if (string.IsNullOrWhiteSpace(asignaturas)) return actual;
        var primera = asignaturas.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var d = DeptoDe(primera) ?? DeptoDe(asignaturas);
        return d ?? (primera is { Length: > 0 } ? primera[..Math.Min(primera.Length, 60)] : actual);
    }

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
        var orden = 1;
        foreach (var (tipo, min) in PlanDoble)
        {
            bloques.Add(new BloqueJornada { ColegioId = colegioId, Orden = orden++, HoraInicio = t, HoraFin = t.Add(TimeSpan.FromMinutes(min)), Tipo = tipo });
            t = t.Add(TimeSpan.FromMinutes(min));
        }
        _context.BloquesJornada.AddRange(bloques);
        await _context.SaveChangesAsync();
        return bloques;
    }

    /// <summary>
    /// Jornada con bloques dobles: cada clase dura 2 horas pedagógicas (90 min) y va de un recreo al otro.
    /// 08:00–09:30 clase · recreo · 09:45–11:15 clase · recreo · 11:30–13:00 clase · almuerzo · 13:45–15:15 clase.
    /// </summary>
    private static readonly (TipoBloque Tipo, int Min)[] PlanDoble =
    {
        (TipoBloque.Clase, 90), (TipoBloque.Recreo, 15),
        (TipoBloque.Clase, 90), (TipoBloque.Recreo, 15),
        (TipoBloque.Clase, 90), (TipoBloque.Almuerzo, 45),
        (TipoBloque.Clase, 90)
    };

    /// <summary>Horas pedagógicas (45 min) que vale un bloque: uno de 90 minutos son 2 horas.</summary>
    public static int HorasBloque(BloqueJornada? b) => b == null ? 1 : Math.Max(1, (int)Math.Round((b.HoraFin - b.HoraInicio).TotalMinutes / 45.0));

    /// <summary>Horas pedagógicas de clase a la semana de cada profesor/a (suma lo que vale cada bloque).</summary>
    private async Task<Dictionary<int, int>> HorasProfesoresAsync(int colegioId, int? soloProfesor = null)
    {
        var valor = await ValorBloquesAsync(colegioId);
        var cl = await _context.ClasesHorario.AsNoTracking()
            .Where(c => c.ColegioId == colegioId && c.ProfesorId != null && (soloProfesor == null || c.ProfesorId == soloProfesor))
            .Select(c => new { P = c.ProfesorId!.Value, c.BloqueJornadaId }).ToListAsync();
        return cl.GroupBy(c => c.P).ToDictionary(g => g.Key, g => g.Sum(x => valor.GetValueOrDefault(x.BloqueJornadaId, 1)));
    }

    private async Task<Dictionary<int, int>> ValorBloquesAsync(int colegioId) =>
        (await _context.BloquesJornada.AsNoTracking().Where(b => b.ColegioId == colegioId).ToListAsync()).ToDictionary(b => b.Id, b => HorasBloque(b));

    private async Task<(TimeSpan Salida, bool Tarde)> ViernesAsync(int colegioId)
    {
        var c = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == colegioId)
            .Select(x => new { x.ViernesSalida, x.ViernesTardeActiva }).FirstOrDefaultAsync();
        return (c?.ViernesSalida ?? new TimeSpan(13, 0, 0), c?.ViernesTardeActiva ?? false);
    }

    /// <summary>Viernes efectivo de un curso: el propio si lo tiene, si no el del colegio.</summary>
    private static (TimeSpan Salida, bool Tarde) ViernesDe(Curso c, (TimeSpan Salida, bool Tarde) col) =>
        c.ViernesTarde == true ? (c.ViernesSalida ?? col.Salida, true)
        : c.ViernesSalida != null ? (c.ViernesSalida.Value, false)
        : col;

    [HttpGet]
    public async Task<IActionResult> Jornada()
    {
        var colegioId = ColegioId();
        // La jornada (bloques, recreos, viernes) solo la ve y la cambia quien arma los horarios (UTP / administración)
        if (!await PuedeEditarAsync()) return RedirectToAction(nameof(Index));
        ViewBag.PuedeEditar = true;
        var (salida, tarde) = await ViernesAsync(colegioId);
        ViewBag.ViernesSalida = salida;
        ViewBag.ViernesTarde = tarde;
        ViewBag.ConClases = await _context.ClasesHorario.CountAsync(c => c.ColegioId == colegioId);
        return View(await JornadaAsync(colegioId));
    }

    /// <summary>
    /// Cambia la jornada a bloques dobles (90 min, de recreo a recreo). Como los bloques cambian,
    /// los horarios armados se borran y se quitan los bloqueos/aprobaciones: después se usa "🪄 Generar horario".
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AplicarBloquesDobles(string? entrada)
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var t = TimeSpan.TryParse(entrada, out var e) ? e : new TimeSpan(8, 0, 0);

        _context.ClasesHorario.RemoveRange(await _context.ClasesHorario.Where(c => c.ColegioId == colegioId).ToListAsync());
        var actuales = await _context.BloquesJornada.Where(b => b.ColegioId == colegioId).OrderBy(b => b.Orden).ToListAsync();
        foreach (var b in actuales) b.Orden += 1000;   // evita choque del índice único mientras se reordena
        await _context.SaveChangesAsync();
        for (var i = 0; i < PlanDoble.Length; i++)
        {
            var b = i < actuales.Count ? actuales[i] : null;
            if (b == null) { b = new BloqueJornada { ColegioId = colegioId }; _context.BloquesJornada.Add(b); }
            b.Orden = i + 1; b.Tipo = PlanDoble[i].Tipo; b.HoraInicio = t; b.HoraFin = t.Add(TimeSpan.FromMinutes(PlanDoble[i].Min));
            t = b.HoraFin;
        }
        if (actuales.Count > PlanDoble.Length) _context.BloquesJornada.RemoveRange(actuales.Skip(PlanDoble.Length));

        // Los horarios aprobados ya no existen: se desbloquean para armarlos de nuevo
        foreach (var c in await _context.Cursos.Where(c => c.ColegioId == colegioId && c.HorarioBloqueado).ToListAsync()) c.HorarioBloqueado = false;
        foreach (var u in await _context.Usuarios.Where(u => u.ColegioId == colegioId && (u.HorarioBloqueado || u.HorarioConfirmadoUtc != null || u.HorarioAprobadoUtc != null)).ToListAsync())
        { u.HorarioBloqueado = false; u.HorarioConfirmadoUtc = null; u.HorarioAprobadoUtc = null; }
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "✅ Jornada con bloques dobles (90 min, de recreo a recreo). Los horarios anteriores se borraron: vuelve a Horarios y usa \"🪄 Generar horario\".";
        return RedirectToAction(nameof(Jornada));
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
        vm.CursoBloqueado = curso.HorarioBloqueado;
        // Alumno/a o apoderado/a: sus clubes aparecen aparte, fuera del horario de clases
        if (User.IsInRole("Estudiante") || User.IsInRole("Apoderado"))
        {
            var yo = UsuarioId();
            var ids = User.IsInRole("Estudiante")
                ? await _context.Estudiantes.Where(e => e.UsuarioId == yo && e.CursoId == id).Select(e => e.Id).ToListAsync()
                : await _context.EstudiantesApoderados.Where(x => x.Apoderado != null && x.Apoderado.UsuarioId == yo && x.Estudiante != null && x.Estudiante.CursoId == id)
                    .Select(x => x.EstudianteId).ToListAsync();
            vm.ClubesFuera = await Auka.Web.Services.Clubes.DeAlumnosAsync(_context, colegioId, ids, conNombre: User.IsInRole("Apoderado") && ids.Count > 1);
        }
        vm.Titulo = await Jefaturas.NombreConEspecialidadAsync(_context, curso);   // "4º Medio B · Mecánica Automotriz"
        vm.Subtitulo = curso.ProfesorJefe != null ? $"Profesor/a jefe: {curso.ProfesorJefe.Nombre} {curso.ProfesorJefe.Apellido}" : "Horario semanal del curso";
        vm.CursoId = id;
        var col = (vm.ViernesSalida, vm.ViernesTarde);
        vm.ViernesColegio = col.ViernesTarde ? "jornada completa" : "salida " + col.ViernesSalida.ToString("hh\\:mm");
        vm.ViernesSalidaColegio = col.ViernesTarde ? TimeSpan.FromHours(24) : col.ViernesSalida;
        (vm.ViernesSalida, vm.ViernesTarde) = ViernesDe(curso, col);
        vm.ViernesPropio = curso.ViernesSalida != null || curso.ViernesTarde != null;
        vm.ProfesorJefeId = curso.ProfesorJefeId;
        vm.Editable = await PuedeEditarAsync() && curso.AnioAcademico >= DateTime.Now.Year;
        if (vm.Editable)
        {
            // Cada profesor/a con su carga: "Alejandra Soto · Lenguaje · 18/30 h"
            var horas = await HorasProfesoresAsync(colegioId);
            vm.Profesores = (await _context.Usuarios.AsNoTracking()
                .Where(u => u.ColegioId == colegioId && u.Activo && (u.Rol == RolUsuario.Docente || u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Director || u.Rol == RolUsuario.Orientadora))
                .OrderBy(u => u.Departamento == null).ThenBy(u => u.Departamento).ThenBy(u => u.Apellido)
                .Select(u => new { u.Id, u.Nombre, u.Apellido, u.Departamento, u.Asignaturas, u.HorasContrato }).ToListAsync())
                .Select(u =>
                {
                    var h = horas.GetValueOrDefault(u.Id);
                    var carga = u.HorasContrato is > 0 ? $"{h}/{u.HorasContrato} h" : $"{h} h";
                    var area = !string.IsNullOrWhiteSpace(u.Asignaturas) ? u.Asignaturas : u.Departamento;
                    return new OpcionVm(u.Id, $"{u.Nombre} {u.Apellido}{(area != null ? " · " + area : "")} · {carga}");
                }).ToList();
            var usadas = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId).Select(c => c.Asignatura).Distinct().ToListAsync();
            vm.Asignaturas = AsignaturasBase.Union(usadas).OrderBy(a => a).ToList();
        }
        return View("Curso", vm);
    }

    /// <summary>
    /// Guarda (o borra, si la asignatura viene vacía) la clase de un bloque.
    /// Si el profesor/a o la sala ya están ocupados a esa hora, avisa y propone bloques libres; con "forzar" se guarda igual.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarClase(int cursoId, int dia, int bloqueId, string? asignatura, int? profesorId, string? sala, bool forzar = false)
    {
        if (!await PuedeEditarAsync()) return Json(new { ok = false, error = "No tienes permiso para modificar horarios." });
        var colegioId = ColegioId();
        if (dia < 1 || dia > 5) return Json(new { ok = false, error = "Día no válido." });
        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId);
        var bloque = await _context.BloquesJornada.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bloqueId && b.ColegioId == colegioId);
        if (curso == null || bloque == null) return Json(new { ok = false, error = "Curso o bloque no encontrado." });
        if (bloque.Tipo != TipoBloque.Clase) return Json(new { ok = false, error = "Ese bloque es recreo o almuerzo." });
        var (salida, tarde) = ViernesDe(curso, await ViernesAsync(colegioId));
        if (dia == 5 && !tarde && bloque.HoraInicio >= salida)
            return Json(new { ok = false, error = $"Este curso sale el viernes a las {salida:hh\\:mm}. Cambia «Viernes de este curso» para recuperar clases." });

        if (curso.HorarioBloqueado)
            return Json(new { ok = false, error = "🔒 El horario de este curso está bloqueado. Desbloquéalo arriba para hacer cambios." });

        var clase = await _context.ClasesHorario.FirstOrDefaultAsync(c => c.CursoId == cursoId && c.Dia == dia && c.BloqueJornadaId == bloqueId);
        var profAnterior = clase?.ProfesorId;
        // Bloque de un docente con horario aprobado y bloqueado: no se toca directo (se pide intercambio o se desbloquea)
        if (profAnterior != null)
        {
            var dueño = await _context.Usuarios.AsNoTracking().Where(u => u.Id == profAnterior && u.HorarioBloqueado).Select(u => u.Nombre + " " + u.Apellido).FirstOrDefaultAsync();
            if (dueño != null)
                return Json(new { ok = false, error = $"🔒 Este bloque es de {dueño} y su horario ya está confirmado y bloqueado. Usa «Mover o intercambiar» para pedirle el cambio, o desbloquea su horario en Horarios del personal." });
        }
        asignatura = (asignatura ?? "").Trim();
        if (asignatura.Length == 0)
        {
            if (clase != null)
            {
                _context.ClasesHorario.Remove(clase);
                await _context.SaveChangesAsync();
                await AvisarCambioAsync(colegioId, profAnterior);
            }
            return Json(new { ok = true });
        }
        if (asignatura.Length > 80) asignatura = asignatura[..80];
        sala = string.IsNullOrWhiteSpace(sala) ? null : sala.Trim()[..Math.Min(sala.Trim().Length, 40)];
        var cuando = $"{Dias[dia - 1].ToLower()} a las {bloque.HoraInicio:hh\\:mm}";

        // Consejo de curso / Orientación: si no eligen profesor/a, va solo el profesor/a jefe del curso
        if (EsDeJefatura(asignatura) && profesorId is not > 0 && curso.ProfesorJefeId != null) profesorId = curso.ProfesorJefeId;

        Usuario? prof = null;
        if (profesorId is > 0)
        {
            prof = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == profesorId && u.ColegioId == colegioId && u.Activo);
            if (prof == null) return Json(new { ok = false, error = "Profesor/a no válido." });
            if (prof.HorarioBloqueado && prof.Id != profAnterior)
                return Json(new { ok = false, error = $"🔒 {prof.Nombre} {prof.Apellido} ya confirmó su horario y está bloqueado: no se le pueden sumar clases sin desbloquearlo antes (Horarios del personal)." });
        }
        else profesorId = null;

        // Un profesor/a no puede estar en dos cursos a la vez: no se guarda y se proponen opciones
        if (prof != null)
        {
            var choque = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
                .FirstOrDefaultAsync(c => c.ProfesorId == profesorId && c.Dia == dia && c.BloqueJornadaId == bloqueId && c.CursoId != cursoId);
            if (choque != null)
            {
                var libres = await LibresAsync(colegioId, cursoId, profesorId!.Value, curso);
                var alternativas = await AlternativasAsync(colegioId, asignatura, prof.Departamento, dia, bloqueId, profesorId.Value);
                return Json(new
                {
                    ok = false, choque = true, bloqueante = true,
                    error = $"{prof.Nombre} {prof.Apellido} ya tiene clase en {choque.Curso?.Nombre} {choque.Curso?.Letra} el {cuando}: no puede estar en dos cursos a la vez.",
                    libres, alternativas
                });
            }
        }

        if (!forzar)
        {
            var avisos = new List<string>();
            if (sala != null)
            {
                var salaOcupada = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
                    .FirstOrDefaultAsync(c => c.ColegioId == colegioId && c.Sala != null && c.Sala.ToLower() == sala.ToLower() && c.Dia == dia && c.BloqueJornadaId == bloqueId && c.CursoId != cursoId);
                if (salaOcupada != null) avisos.Add($"La sala «{sala}» ya la usa {salaOcupada.Curso?.Nombre} {salaOcupada.Curso?.Letra} el {cuando}.");
            }
            if (avisos.Count > 0)
                return Json(new { ok = false, choque = true, error = string.Join(" ", avisos), libres = new List<string>() });
        }

        if (clase == null)
        {
            clase = new ClaseHorario { ColegioId = colegioId, CursoId = cursoId, Dia = dia, BloqueJornadaId = bloqueId };
            _context.ClasesHorario.Add(clase);
        }
        clase.Asignatura = asignatura;
        clase.ProfesorId = profesorId;
        clase.Sala = sala;
        clase.ModificadaUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await AvisarCambioAsync(colegioId, profesorId);
        if (profAnterior != profesorId) await AvisarCambioAsync(colegioId, profAnterior);

        // Carga horaria: si se pasa de su contrato, se avisa (se guarda igual)
        string? aviso = null;
        if (prof?.HorasContrato is > 0)
        {
            var horas = (await HorasProfesoresAsync(colegioId, profesorId)).GetValueOrDefault(profesorId!.Value);
            if (horas > prof.HorasContrato) aviso = $"Cuidado: {prof.Nombre} {prof.Apellido} queda con {horas} horas y su contrato es de {prof.HorasContrato}.";
        }
        return Json(new { ok = true, aviso });
    }

    /// <summary>
    /// Profesores/as libres en ese día y bloque que pueden hacer la asignatura: primero los del mismo departamento
    /// o que ya la hacen en otros cursos, con menos carga primero.
    /// </summary>
    private async Task<List<object>> AlternativasAsync(int colegioId, string asignatura, string? deptoProf, int dia, int bloqueId, int excluirId)
    {
        var depto = DeptoDe(asignatura) ?? deptoProf;
        var ocupados = await _context.ClasesHorario.AsNoTracking()
            .Where(c => c.ColegioId == colegioId && c.Dia == dia && c.BloqueJornadaId == bloqueId && c.ProfesorId != null)
            .Select(c => c.ProfesorId!.Value).ToListAsync();
        var hacenAsig = await _context.ClasesHorario.AsNoTracking()
            .Where(c => c.ColegioId == colegioId && c.ProfesorId != null && c.Asignatura.ToLower() == asignatura.ToLower())
            .Select(c => c.ProfesorId!.Value).Distinct().ToListAsync();
        var horas = await HorasProfesoresAsync(colegioId);
        var clave = asignatura.Trim().ToLower();
        clave = clave.Length > 6 ? clave[..6] : clave;   // "lenguaje y comunicación" ≈ "lengua", "matemática" ≈ "matemá"
        var candidatos = (await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente && u.Id != excluirId && !ocupados.Contains(u.Id))
            .Select(u => new { u.Id, u.Nombre, u.Apellido, u.Departamento, u.Asignaturas, u.HorasContrato }).ToListAsync())
            .Where(u => (depto != null && u.Departamento == depto) || hacenAsig.Contains(u.Id)
                || (u.Asignaturas != null && (u.Asignaturas.ToLower().Contains(clave) || (depto != null && DeptoDe(u.Asignaturas) == depto))))
            .ToList();
        return candidatos
            .Where(u => u.HorasContrato is not > 0 || horas.GetValueOrDefault(u.Id) < u.HorasContrato)   // no proponer a quien ya completó su contrato
            .OrderBy(u => horas.GetValueOrDefault(u.Id))
            .Take(5)
            .Select(u => (object)new
            {
                id = u.Id,
                nombre = $"{u.Nombre} {u.Apellido}",
                detalle = (u.Asignaturas ?? u.Departamento ?? "Docente") + " · " + horas.GetValueOrDefault(u.Id) + (u.HorasContrato is > 0 ? $"/{u.HorasContrato}" : "") + " h"
            }).ToList();
    }

    /// <summary>Opciones libres para una asignatura en un día y bloque (las muestra el formulario al escribir la asignatura).</summary>
    [HttpGet]
    public async Task<IActionResult> Sugeridos(int dia, int bloqueId, string? asignatura)
    {
        if (string.IsNullOrWhiteSpace(asignatura)) return Json(Array.Empty<object>());
        return Json(await AlternativasAsync(ColegioId(), asignatura.Trim(), null, dia, bloqueId, 0));
    }

    /// <summary>Quién está ocupado en un día y bloque (para marcarlo en la lista al asignar).</summary>
    [HttpGet]
    public async Task<IActionResult> Ocupados(int cursoId, int dia, int bloqueId)
    {
        var colegioId = ColegioId();
        var l = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
            .Where(c => c.ColegioId == colegioId && c.Dia == dia && c.BloqueJornadaId == bloqueId && c.CursoId != cursoId)
            .Select(c => new { c.ProfesorId, c.Sala, Curso = c.Curso!.Nombre + " " + c.Curso.Letra }).ToListAsync();
        return Json(new
        {
            profesores = l.Where(x => x.ProfesorId != null).GroupBy(x => x.ProfesorId!.Value).ToDictionary(g => g.Key.ToString(), g => g.First().Curso.Trim()),
            salas = l.Where(x => x.Sala != null).Select(x => x.Sala).Distinct().ToList()
        });
    }

    private async Task<List<string>> LibresAsync(int colegioId, int cursoId, int profesorId, Curso curso)
    {
        var bloques = (await JornadaAsync(colegioId)).Where(b => b.Tipo == TipoBloque.Clase).ToList();
        var (salida, tarde) = ViernesDe(curso, await ViernesAsync(colegioId));
        var delCurso = await _context.ClasesHorario.AsNoTracking().Where(c => c.CursoId == cursoId).Select(c => new { c.Dia, c.BloqueJornadaId }).ToListAsync();
        var delProf = await _context.ClasesHorario.AsNoTracking().Where(c => c.ProfesorId == profesorId).Select(c => new { c.Dia, c.BloqueJornadaId }).ToListAsync();
        var r = new List<string>();
        for (var d = 1; d <= 5 && r.Count < 6; d++)
            foreach (var b in bloques)
            {
                if (d == 5 && !tarde && b.HoraInicio >= salida) continue;
                if (delCurso.Any(x => x.Dia == d && x.BloqueJornadaId == b.Id) || delProf.Any(x => x.Dia == d && x.BloqueJornadaId == b.Id)) continue;
                r.Add($"{Dias[d - 1]} {b.HoraInicio:hh\\:mm}");
                if (r.Count >= 6) break;
            }
        return r;
    }

    /// <summary>Avisa al docente (una vez al día) que su horario cambió y que debe revisarlo y confirmarlo.</summary>
    private async Task AvisarCambioAsync(int colegioId, int? profesorId)
    {
        if (profesorId == null || profesorId == UsuarioId()) return;
        const string titulo = "🗓️ Tu horario fue actualizado";
        var hoy = DateTime.UtcNow.Date;
        if (await _context.Notificaciones.AnyAsync(n => n.UsuarioId == profesorId && n.Titulo == titulo && n.CreadaEnUtc >= hoy)) return;
        _context.Notificaciones.Add(new Notificacion
        {
            ColegioId = colegioId, UsuarioId = profesorId.Value, Titulo = titulo,
            Mensaje = "La UTP hizo cambios en tu horario. Revísalo y confirma si estás de acuerdo.",
            Enlace = $"/Horarios/Profesor/{profesorId}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
        });
        await _context.SaveChangesAsync();
    }

    /// <summary>Viernes propio del curso: "colegio" (como todos), "salida" (otra hora) o "tarde" (jornada completa / recuperación).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarViernesCurso(int cursoId, string modo, string? salida)
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        var colegioId = ColegioId();
        string mensaje;
        if (modo == "salida" && TimeSpan.TryParse(salida, out var t))
        {
            curso.ViernesSalida = t;
            curso.ViernesTarde = false;
            mensaje = $"El viernes {curso.Nombre} {curso.Letra} sale a las {t:hh\\:mm}. Ya puedes asignar las asignaturas de ese día.";

            // Si sale después del último bloque de la jornada, se agregan los bloques que faltan (misma duración)
            var bloques = await JornadaAsync(colegioId);
            var ultimo = bloques.Last();
            var dur = bloques.Where(b => b.Tipo == TipoBloque.Clase).Select(b => b.HoraFin - b.HoraInicio).FirstOrDefault();
            if (dur <= TimeSpan.Zero) dur = TimeSpan.FromMinutes(45);
            var agregados = 0;
            var fin = ultimo.HoraFin;
            var orden = ultimo.Orden;
            while (fin < t && agregados < 4)
            {
                _context.BloquesJornada.Add(new BloqueJornada { ColegioId = colegioId, Orden = ++orden, HoraInicio = fin, HoraFin = fin + dur, Tipo = TipoBloque.Clase });
                fin += dur;
                agregados++;
            }
            if (agregados > 0) mensaje += $" Se agregaron {agregados} bloque(s) al final de la jornada para la recuperación.";
        }
        else
        {
            curso.ViernesSalida = null; curso.ViernesTarde = null;
            mensaje = $"El viernes {curso.Nombre} {curso.Letra} vuelve a salir como el resto del colegio.";
        }
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = mensaje;
        return Redirect($"/Horarios/Curso/{cursoId}");
    }

    // ---------------------------------------------------------------- Horario automático (borrador)
    /// <summary>Plan de estudio base por nivel (bloques por semana). Es un borrador: después la UTP ajusta a mano.</summary>
    private static List<(string Asig, int Horas)> PlanDe(string nivel, string? especialidad)
    {
        var o = Niveles.Orden(nivel);   // 0 prekínder, 1 kínder, 2..9 = 1º..8º básico, 10..13 = 1º..4º medio
        if (o <= 1) return new() { ("Lenguaje y Comunicación", 8), ("Matemática", 6), ("Ciencias Naturales", 4), ("Educación Física y Salud", 4), ("Artes Visuales", 4), ("Música", 2), ("Orientación y Consejo de Curso", 2) };
        if (o >= 2 && o <= 7) return new() { ("Lenguaje y Comunicación", 8), ("Matemática", 6), ("Historia, Geografía y Cs. Sociales", 3), ("Ciencias Naturales", 3), ("Inglés", 2), ("Educación Física y Salud", 4), ("Artes Visuales", 2), ("Música", 2), ("Tecnología", 1), ("Religión", 2), ("Orientación y Consejo de Curso", 2) };
        if (o == 8 || o == 9) return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 6), ("Historia, Geografía y Cs. Sociales", 4), ("Ciencias Naturales", 4), ("Inglés", 3), ("Educación Física y Salud", 2), ("Artes Visuales", 2), ("Música", 2), ("Tecnología", 1), ("Religión", 2), ("Orientación y Consejo de Curso", 2) };
        // 1º y 2º medio con especialidad (colegios TP desde 1º medio): Artes y Tecnología se reemplazan por la especialidad
        if ((o == 10 || o == 11) && !string.IsNullOrWhiteSpace(especialidad))
            return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 7), ("Historia, Geografía y Cs. Sociales", 4), ("Biología", 2), ("Química", 2), ("Física", 2), ("Inglés", 4), ("Educación Física y Salud", 2), ("Orientación y Consejo de Curso", 2), ($"Especialidad TP - {especialidad}", 4) };
        if (o == 10 || o == 11) return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 7), ("Historia, Geografía y Cs. Sociales", 4), ("Biología", 2), ("Química", 2), ("Física", 2), ("Inglés", 4), ("Educación Física y Salud", 2), ("Artes Visuales", 2), ("Tecnología", 2), ("Orientación y Consejo de Curso", 2) };
        // 3º y 4º medio TP: primero la formación general (ramos comunes) y al final la especialidad,
        // para que si la jornada no alcanza, lo que se recorte sean horas de especialidad y no los ramos básicos.
        if (o >= 12 && !string.IsNullOrWhiteSpace(especialidad))
            return new() { ("Lenguaje y Comunicación", 3), ("Matemática", 3), ("Inglés", 2), ("Educación Ciudadana", 2), ("Filosofía", 2),
                           ("Ciencias para la Ciudadanía", 2), ("Historia, Geografía y Cs. Sociales", 2), ("Educación Física y Salud", 2),
                           ("Orientación y Consejo de Curso", 2), ($"Especialidad TP - {especialidad}", 18) };
        if (o >= 12) return new() { ("Lenguaje y Comunicación", 3), ("Matemática", 3), ("Educación Ciudadana", 2), ("Filosofía", 2), ("Ciencias para la Ciudadanía", 2), ("Inglés", 2), ("Educación Física y Salud", 2), ("Biología", 3), ("Química", 3), ("Física", 3), ("Historia, Geografía y Cs. Sociales", 3), ("Orientación y Consejo de Curso", 2) };
        return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 6), ("Orientación y Consejo de Curso", 2) };
    }

    /// <summary>
    /// Arma un borrador de horario: llena los bloques libres de cada curso con su plan de estudio, eligiendo profesores/as
    /// del área que estén libres (sin choques) y con menos carga; Consejo de Curso y Orientación van al profesor/a jefe.
    /// No toca las clases que ya existen (salvo "rehacer" en un curso).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerarHorario(int? cursoId, bool rehacer = false)
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var anio = DateTime.Now.Year;
        var bloques = (await JornadaAsync(colegioId)).Where(b => b.Tipo == TipoBloque.Clase).OrderBy(b => b.HoraInicio).ToList();
        var colViernes = await ViernesAsync(colegioId);

        var cursos = await _context.Cursos.Include(c => c.Estudiantes)
            .Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.Nombre != "Pacientes" && (cursoId == null || c.Id == cursoId))
            .ToListAsync();
        var cursosBloqueados = cursos.Count(c => c.HorarioBloqueado);
        cursos = cursos.Where(c => !c.HorarioBloqueado).OrderBy(c => Niveles.Orden(c.Nombre)).ThenBy(c => c.Letra).ToList();
        // Docentes con horario aprobado y bloqueado: sus clases no se mueven y no se les suman horas
        var profBloqueados = (await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.HorarioBloqueado).Select(u => u.Id).ToListAsync()).ToHashSet();
        if (cursoId != null && rehacer && cursos.Count > 0)
        {
            // Rehacer: se borra lo que no está bloqueado; los bloques de docentes bloqueados se quedan donde están
            _context.ClasesHorario.RemoveRange(await _context.ClasesHorario
                .Where(c => c.CursoId == cursoId && (c.ProfesorId == null || !profBloqueados.Contains(c.ProfesorId.Value))).ToListAsync());
            await _context.SaveChangesAsync();
        }

        // Todos los cursos con profesor/a jefe (Orientación y Consejo de Curso son suyos). En 3º y 4º TP, de la especialidad.
        var jefes = await Jefaturas.AsignarAsync(_context, colegioId, anio, corregirTp: false, cursos.Where(c => c.ProfesorJefeId == null).Select(c => c.Id));

        var existentes = await _context.ClasesHorario.Where(c => c.ColegioId == colegioId).ToListAsync();
        var cursoOcupado = existentes.Select(c => (c.CursoId, c.Dia, c.BloqueJornadaId)).ToHashSet();
        var profOcupado = existentes.Where(c => c.ProfesorId != null).Select(c => (c.ProfesorId!.Value, c.Dia, c.BloqueJornadaId)).ToHashSet();
        var valor = await ValorBloquesAsync(colegioId);   // 1 bloque de 90 min = 2 horas pedagógicas
        int V(int bloqueId) => valor.GetValueOrDefault(bloqueId, 1);
        var carga = existentes.Where(c => c.ProfesorId != null).GroupBy(c => c.ProfesorId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => V(x.BloqueJornadaId)));

        var docentes = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente && !u.HorarioBloqueado)
            .Select(u => new { u.Id, u.Asignaturas, u.Departamento, u.HorasContrato }).ToListAsync();
        int Tope(int id) { var h = docentes.FirstOrDefault(d => d.Id == id)?.HorasContrato ?? 0; return h > 0 ? h : 38; }
        // Ciclo de cada docente (básica / media): por sus clases y su jefatura; se va completando al armar
        var ciclos = (await Jefaturas.DocentesAsync(_context, colegioId, anio)).ToDictionary(d => d.Id, d => d.Ciclos);

        int asignados = 0, sinProfesor = 0, sinEspacio = 0;
        var faltan = new Dictionary<string, int>();
        var nuevos = new List<ClaseHorario>();

        foreach (var curso in cursos)
        {
            var (salida, tarde) = ViernesDe(curso, colViernes);
            var slots = new List<(int Dia, BloqueJornada B)>();
            for (var d = 1; d <= 5; d++)
                foreach (var b in bloques)
                    if (!(d == 5 && !tarde && b.HoraInicio >= salida)) slots.Add((d, b));

            var especialidad = curso.Estudiantes.Where(e => !string.IsNullOrEmpty(e.Especialidad)).GroupBy(e => e.Especialidad!).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var porDia = new Dictionary<(string, int), int>();   // cuántas horas de cada asignatura hay ya ese día (para repartir)

            // Lo que ya está puesto cuenta: si un bloqueado ya hace 3 horas de Matemática, solo faltan las demás
            var yaTiene = existentes.Where(c => c.CursoId == curso.Id).GroupBy(c => c.Asignatura.Trim().ToLower()).ToDictionary(g => g.Key, g => g.Sum(x => V(x.BloqueJornadaId)));
            foreach (var (asig, horasPlan) in PlanDe(curso.Nombre, especialidad))
            {
                var horas = Math.Max(0, horasPlan - yaTiene.GetValueOrDefault(asig.ToLower()));
                if (horas == 0) continue;
                // Profesor/a para esta asignatura en este curso (el mismo para todas sus horas, si se puede)
                List<int> candidatos;
                if (EsDeJefatura(asig)) candidatos = curso.ProfesorJefeId != null && !profBloqueados.Contains(curso.ProfesorJefeId.Value) ? new List<int> { curso.ProfesorJefeId.Value } : new List<int>();
                else
                {
                    var depto = DeptoDe(asig);
                    var clave = asig.StartsWith("Especialidad TP - ") ? asig["Especialidad TP - ".Length..].ToLower() : asig.ToLower()[..Math.Min(asig.Length, 6)];
                    var cicloCurso = Niveles.Ciclo(curso.Nombre);
                    candidatos = docentes.Where(d =>
                            (d.Asignaturas != null && d.Asignaturas.ToLower().Contains(clave))
                            || (!asig.StartsWith("Especialidad") && depto != null && (d.Departamento == depto || DeptoDe(d.Asignaturas) == depto)))
                        // 1º el profesor/a jefe (así ve más días a su curso), 2º los del mismo ciclo, 3º menos carga
                        .OrderBy(d => d.Id == curso.ProfesorJefeId ? 0 : 1)
                        .ThenBy(d => ciclos.TryGetValue(d.Id, out var cs) && cs.Count > 0 && !cs.Contains(cicloCurso) ? 1 : 0)
                        .ThenBy(d => carga.GetValueOrDefault(d.Id)).Select(d => d.Id).ToList();
                }

                var restantes = horas;
                while (restantes > 0)
                {
                    // Bloques libres del curso: primero los que no se pasan de las horas que faltan, los días con menos horas de esta asignatura y temprano
                    var falta = restantes;
                    var libres = slots.Where(sl => !cursoOcupado.Contains((curso.Id, sl.Dia, sl.B.Id)))
                        .OrderBy(sl => V(sl.B.Id) > falta ? 1 : 0).ThenBy(sl => porDia.GetValueOrDefault((asig, sl.Dia))).ThenBy(sl => sl.B.HoraInicio).ThenBy(sl => sl.Dia).ToList();
                    if (libres.Count == 0) { sinEspacio += restantes; break; }

                    (int Dia, BloqueJornada B)? elegido = null; int? prof = null;
                    foreach (var sl in libres)
                    {
                        var p = candidatos.FirstOrDefault(c => !profOcupado.Contains((c, sl.Dia, sl.B.Id)) && carga.GetValueOrDefault(c) + V(sl.B.Id) <= Tope(c));
                        if (p != 0) { elegido = sl; prof = p; break; }
                    }
                    if (elegido == null) { elegido = libres[0]; prof = null; sinProfesor++; faltan[asig] = faltan.GetValueOrDefault(asig) + 1; }

                    var (dia, bl) = elegido.Value;
                    nuevos.Add(new ClaseHorario { ColegioId = colegioId, CursoId = curso.Id, Dia = dia, BloqueJornadaId = bl.Id, Asignatura = asig, ProfesorId = prof, ModificadaUtc = DateTime.UtcNow });
                    cursoOcupado.Add((curso.Id, dia, bl.Id));
                    var vale = V(bl.Id);
                    restantes -= vale;
                    porDia[(asig, dia)] = porDia.GetValueOrDefault((asig, dia)) + vale;
                    if (prof != null)
                    {
                        profOcupado.Add((prof.Value, dia, bl.Id));
                        carga[prof.Value] = carga.GetValueOrDefault(prof.Value) + vale;
                        if (!ciclos.TryGetValue(prof.Value, out var cp)) ciclos[prof.Value] = cp = new HashSet<string>();
                        cp.Add(Niveles.Ciclo(curso.Nombre));
                        // El mismo profesor/a sigue con las demás horas de esta asignatura en este curso
                        candidatos = candidatos.OrderBy(c => c == prof ? 0 : 1).ThenBy(c => carga.GetValueOrDefault(c)).ToList();
                    }
                    asignados++;
                }
            }
        }

        _context.ClasesHorario.AddRange(nuevos);
        await _context.SaveChangesAsync();
        var msg = $"🪄 Borrador listo: {asignados} bloques asignados en {cursos.Count} {(cursos.Count == 1 ? "curso" : "cursos")}.";
        if (jefes.Asignados > 0) msg += $" Se asignaron {jefes.Asignados} profesores/as jefe que faltaban.";
        if (jefes.Faltan.Count > 0) msg += $" ⚠️ Sin profesor/a jefe: {string.Join("; ", jefes.Faltan.Take(6))}. Agrega docentes en Personal.";
        if (sinProfesor > 0) msg += $" {sinProfesor} quedaron sin profesor/a (falta personal o asignatura en Personal): " + string.Join(", ", faltan.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} ({x.Value})")) + ".";
        if (sinEspacio > 0) msg += $" {sinEspacio} horas no cupieron en la jornada.";
        if (cursosBloqueados > 0) msg += $" {cursosBloqueados} {(cursosBloqueados == 1 ? "curso bloqueado no se tocó" : "cursos bloqueados no se tocaron")}.";
        if (profBloqueados.Count > 0) msg += " Los bloques de docentes con horario aprobado (🔒) se respetaron.";
        if (asignados == 0 && cursos.Count > 0)
            msg = "ℹ️ No se agregó nada: los horarios de los cursos ya están llenos (el botón solo completa bloques vacíos). Para armarlos de nuevo con bloques dobles: Jornada y viernes → \"Aplicar bloques dobles\" (borra los horarios actuales) y después vuelve a apretar este botón.";
        else msg += " Revísalo y ajústalo tocando cada bloque.";
        TempData["SuccessMessage"] = msg;
        return cursoId != null ? Redirect($"/Horarios/Curso/{cursoId}") : RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Completa los bloques que tienen asignatura pero no profesor/a: busca a quien hace esa asignatura, es del ciclo del curso,
    /// está libre a esa hora y no pasa sus horas de contrato. Prefiere a quien ya la hace en ese curso y al profesor/a jefe.
    /// Orientación y Consejo de Curso → profesor/a jefe. No toca cursos ni docentes con horario bloqueado.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompletarProfesores(int? cursoId)
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var anio = DateTime.Now.Year;
        await Jefaturas.AsignarAsync(_context, colegioId, anio, corregirTp: false,
            await _context.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.ProfesorJefeId == null && (cursoId == null || c.Id == cursoId)).Select(c => c.Id).ToListAsync());
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && !c.HorarioBloqueado && (cursoId == null || c.Id == cursoId))
            .ToDictionaryAsync(c => c.Id);
        var todas = await _context.ClasesHorario.Where(c => c.ColegioId == colegioId).ToListAsync();
        var valor = await ValorBloquesAsync(colegioId);
        int V(int b) => valor.GetValueOrDefault(b, 1);
        var docentes = (await Jefaturas.DocentesAsync(_context, colegioId, anio)).ToDictionary(d => d.Id);
        var bloqueados = (await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.HorarioBloqueado).Select(u => u.Id).ToListAsync()).ToHashSet();
        var carga = todas.Where(c => c.ProfesorId != null).GroupBy(c => c.ProfesorId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => V(x.BloqueJornadaId)));
        var ocupado = todas.Where(c => c.ProfesorId != null).Select(c => (c.ProfesorId!.Value, c.Dia, c.BloqueJornadaId)).ToHashSet();
        int Tope(int id) => docentes.TryGetValue(id, out var d) && d.HorasContrato is > 0 ? d.HorasContrato.Value : 38;

        int completados = 0;
        var faltan = new Dictionary<string, int>();
        foreach (var cl in todas.Where(c => c.ProfesorId == null && cursos.ContainsKey(c.CursoId) && !string.IsNullOrWhiteSpace(c.Asignatura))
                     .OrderBy(c => Niveles.Orden(cursos[c.CursoId].Nombre)).ThenBy(c => c.CursoId).ThenBy(c => c.Dia))
        {
            var curso = cursos[cl.CursoId];
            var ciclo = Niveles.Ciclo(curso.Nombre);
            var vale = V(cl.BloqueJornadaId);
            var mismoRamo = todas.Where(x => x.CursoId == cl.CursoId && x.ProfesorId != null && x.Asignatura == cl.Asignatura).Select(x => x.ProfesorId!.Value).ToHashSet();
            IEnumerable<Jefaturas.Docente> cand = EsDeJefatura(cl.Asignatura)
                ? docentes.Values.Where(d => d.Id == curso.ProfesorJefeId)
                : docentes.Values.Where(d => Jefaturas.Hace(d, cl.Asignatura) && Jefaturas.DelCiclo(d, ciclo));
            var elegido = cand.Where(d => !bloqueados.Contains(d.Id) && !ocupado.Contains((d.Id, cl.Dia, cl.BloqueJornadaId)) && carga.GetValueOrDefault(d.Id) + vale <= Tope(d.Id))
                .OrderBy(d => mismoRamo.Contains(d.Id) ? 0 : 1)
                .ThenBy(d => d.Id == curso.ProfesorJefeId ? 0 : 1)
                .ThenBy(d => d.Ciclos.Contains(ciclo) ? 0 : 1)
                .ThenBy(d => carga.GetValueOrDefault(d.Id))
                .FirstOrDefault();
            if (elegido == null) { faltan[cl.Asignatura] = faltan.GetValueOrDefault(cl.Asignatura) + 1; continue; }
            cl.ProfesorId = elegido.Id; cl.ModificadaUtc = DateTime.UtcNow;
            ocupado.Add((elegido.Id, cl.Dia, cl.BloqueJornadaId));
            carga[elegido.Id] = carga.GetValueOrDefault(elegido.Id) + vale;
            elegido.Ciclos.Add(ciclo);
            completados++;
        }
        await _context.SaveChangesAsync();
        var msg = completados == 0 && faltan.Count == 0 ? "✅ Todos los bloques ya tienen profesor/a."
            : $"👩‍🏫 Se completaron {completados} bloques con profesor/a.";
        if (faltan.Count > 0)
            msg += $" ⚠️ Siguen sin profesor/a {faltan.Values.Sum()} bloques: " + string.Join(", ", faltan.OrderByDescending(x => x.Value).Take(8).Select(x => $"{x.Key} ({x.Value})"))
                + ". Faltan docentes de esas asignaturas (o ya completaron sus horas): agrégalos en Personal y vuelve a apretar el botón.";
        TempData["SuccessMessage"] = msg;
        return cursoId != null ? Redirect($"/Horarios/Curso/{cursoId}") : RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Asigna profesor/a jefe a los cursos que no tienen y, en 3º y 4º medio TP, cambia a quien no es de la especialidad.
    /// Orientación y Consejo de Curso pasan al nuevo jefe. Si no alcanza el personal, dice qué falta.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AsignarJefes()
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var r = await Jefaturas.AsignarAsync(_context, ColegioId(), DateTime.Now.Year, corregirTp: true);
        var msg = r.Asignados + r.Corregidos == 0 && r.Faltan.Count == 0
            ? "✅ Todos los cursos ya tienen profesor/a jefe y en enseñanza media son profes de media o de especialidad."
            : $"👩‍🏫 Profesores/as jefe: {r.Asignados} asignados, {r.Corregidos} cambiados (profe de básica en media o de especialidad en básica)."
              + (r.Cambios.Count > 0 ? " " + string.Join(" · ", r.Cambios.Take(12)) + (r.Cambios.Count > 12 ? " …" : "") : "")
              + (r.Faltan.Count > 0 ? $" ⚠️ Quedan sin jefe: {string.Join("; ", r.Faltan)}. Agrega docentes en Personal y vuelve a apretar el botón." : "");
        TempData["SuccessMessage"] = msg;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Profesores/as para una asignatura en un bloque, ordenados: primero los que la hacen, son del ciclo del curso
    /// (básica / media) y están libres; después los que la hacen pero están ocupados o son de otro ciclo; al final el resto.
    /// Orientación y Consejo de Curso → el profesor/a jefe.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ProfesoresPara(int cursoId, int dia, int bloqueId, string? asignatura)
    {
        var colegioId = ColegioId();
        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId && c.ColegioId == colegioId);
        if (curso == null) return Json(Array.Empty<object>());
        var ciclo = Niveles.Ciclo(curso.Nombre);
        var asig = (asignatura ?? "").Trim();
        var docentes = await Jefaturas.DocentesAsync(_context, colegioId, curso.AnioAcademico);
        var horas = await HorasProfesoresAsync(colegioId);
        var ocupados = (await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
                .Where(c => c.ColegioId == colegioId && c.Dia == dia && c.BloqueJornadaId == bloqueId && c.ProfesorId != null && c.CursoId != cursoId)
                .Select(c => new { P = c.ProfesorId!.Value, Curso = c.Curso!.Nombre + " " + c.Curso.Letra }).ToListAsync())
            .GroupBy(x => x.P).ToDictionary(g => g.Key, g => g.First().Curso.Trim());
        var yaLaHacen = asig.Length == 0 ? new HashSet<int>() : (await _context.ClasesHorario.AsNoTracking()
            .Where(c => c.ColegioId == colegioId && c.ProfesorId != null && c.Asignatura.ToLower() == asig.ToLower()).Select(c => c.ProfesorId!.Value).Distinct().ToListAsync()).ToHashSet();
        var jefatura = EsDeJefatura(asig);
        var corto = ciclo.Replace("Educación ", "");

        var lista = docentes.Select(d =>
        {
            var hace = asig.Length > 0 && (jefatura ? d.Id == curso.ProfesorJefeId : Jefaturas.Hace(d, asig, yaLaHacen));
            var delCiclo = Jefaturas.DelCiclo(d, ciclo);
            var ocupado = ocupados.TryGetValue(d.Id, out var en) ? en : null;
            var h = horas.GetValueOrDefault(d.Id);
            var lleno = d.HorasContrato is > 0 && h >= d.HorasContrato;
            var grupo = hace && delCiclo && ocupado == null && !lleno ? 0 : hace ? 1 : 2;
            var nota = ocupado != null ? $"⛔ ocupado en {ocupado}" : lleno ? "completó su contrato" : !delCiclo ? $"hace clases en {string.Join(" y ", d.Ciclos.Select(c => c.Replace("Educación ", "")))}" : "libre";
            return new
            {
                id = d.Id, grupo, jefe = d.Id == curso.ProfesorJefeId,
                nombre = d.Nombre + (d.Id == curso.ProfesorJefeId ? " (profe jefe)" : ""),
                detalle = $"{d.Asignaturas ?? d.Departamento ?? "Docente"} · {h}{(d.HorasContrato is > 0 ? "/" + d.HorasContrato : "")} h · {nota}",
                orden = h
            };
        })
        .OrderBy(x => x.grupo).ThenBy(x => x.jefe ? 0 : 1).ThenBy(x => x.orden).ToList();
        return Json(new
        {
            titulos = new[]
            {
                jefatura ? "👩‍🏫 Profesor/a jefe del curso" : $"✅ Hacen {asig} en {corto} y están libres",
                jefatura ? "—" : $"⏳ Hacen {asig}, pero están ocupados, completos o son de otro ciclo",
                "Otros docentes"
            },
            lista
        });
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

        // Docentes ingresados antes: su departamento sale de las asignaturas escritas en Personal (una sola vez)
        var sinDepto = personal.Where(u => u.Departamento == null && !string.IsNullOrWhiteSpace(u.Asignaturas)).Select(u => u.Id).ToList();
        if (sinDepto.Count > 0)
        {
            foreach (var u in await _context.Usuarios.Where(x => sinDepto.Contains(x.Id)).ToListAsync())
                u.Departamento = DepartamentoDesde(u.Asignaturas, null);
            await _context.SaveChangesAsync();
            personal = await _context.Usuarios.AsNoTracking()
                .Where(u => u.ColegioId == colegioId && u.Activo && (u.Rol == RolUsuario.Docente || u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Director
                    || u.Rol == RolUsuario.Inspector || u.Rol == RolUsuario.Orientadora || u.Rol == RolUsuario.SuperAdmin))
                .OrderBy(u => u.Apellido).ToListAsync();
        }
        ViewBag.Horas = await HorasProfesoresAsync(colegioId);
        // Cursos de cada docente y en qué ciclo hace clases (básica / media)
        var cursosProf = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
            .Where(c => c.ColegioId == colegioId && c.ProfesorId != null)
            .Select(c => new { P = c.ProfesorId!.Value, c.Curso!.Nombre, c.Curso.Letra, c.ModificadaUtc }).ToListAsync();
        ViewBag.Cursos = cursosProf.GroupBy(x => x.P).ToDictionary(g => g.Key,
            g => g.Select(x => (x.Nombre + " " + x.Letra).Trim()).Distinct().OrderBy(n => Niveles.Orden(n)).ThenBy(n => n).ToList());
        ViewBag.Ciclos = cursosProf.GroupBy(x => x.P).ToDictionary(g => g.Key, g => g.Select(x => Niveles.Ciclo(x.Nombre)).Distinct().ToList());
        ViewBag.UltimoCambio = cursosProf.GroupBy(x => x.P).ToDictionary(g => g.Key, g => g.Max(x => x.ModificadaUtc));
        ViewBag.PuedeEditar = await PuedeEditarAsync();
        if (ViewBag.PuedeEditar == true)
        {
            var sols = await _context.SolicitudesCambioHorario.AsNoTracking()
                .Where(x => x.Estado == EstadoSolicitudHorario.Pendiente || x.RespondidaUtc >= DateTime.UtcNow.AddDays(-7))
                .OrderBy(x => x.Estado).ThenByDescending(x => x.CreadaUtc).Take(20).ToListAsync();
            ViewBag.Solicitudes = sols;
            var nombres = personal.ToDictionary(p => p.Id, p => p.Nombre + " " + p.Apellido);
            ViewBag.Nombres = nombres;
        }
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
        // Misma información en todo AUKA: si en Personal no tenía asignatura, queda la del departamento
        if (string.IsNullOrWhiteSpace(u.Asignaturas) && u.Departamento != null) u.Asignaturas = u.Departamento;
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AsignarHoras(int usuarioId, int? horas)
    {
        if (!await PuedeEditarAsync()) return Json(new { ok = false, error = "No tienes permiso." });
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.ColegioId == ColegioId());
        if (u == null) return Json(new { ok = false, error = "Funcionario/a no encontrado." });
        u.HorasContrato = horas is > 0 and <= 60 ? horas : null;
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    /// <summary>El docente confirma que está de acuerdo con su horario.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarHorario()
    {
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == UsuarioId());
        if (u == null) return NotFound();
        u.HorarioConfirmadoUtc = DateTime.UtcNow;
        u.HorarioObservacion = null;
        await _context.SaveChangesAsync();
        await AvisarUtpAsync(u.ColegioId, "✅ Horario confirmado", $"{u.Nombre} {u.Apellido} está de acuerdo con su horario. Apruébalo para dejarlo bloqueado 🔒.", $"/Horarios/Profesor/{u.Id}");
        TempData["SuccessMessage"] = "¡Gracias! Quedó registrado que estás de acuerdo con tu horario. La UTP lo revisará para dejarlo fijo.";
        return RedirectToAction(nameof(Profesor), new { id = u.Id });
    }

    /// <summary>El docente no está de acuerdo: deja lo que le complica y le llega a la UTP.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NoDeAcuerdo(string? motivo)
    {
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == UsuarioId());
        if (u == null) return NotFound();
        motivo = (motivo ?? "").Trim();
        if (motivo.Length < 5) { TempData["Error"] = "Cuéntale a la UTP qué no te calza (por ejemplo: «el jueves a las 8:00 tengo un trámite»)."; return RedirectToAction(nameof(Profesor), new { id = u.Id }); }
        u.HorarioObservacion = motivo.Length > 500 ? motivo[..500] : motivo;
        u.HorarioConfirmadoUtc = null;
        u.HorarioBloqueado = false;
        u.HorarioAprobadoUtc = null;
        await _context.SaveChangesAsync();
        await AvisarUtpAsync(u.ColegioId, "✋ Un docente no está de acuerdo con su horario", $"{u.Nombre} {u.Apellido}: «{u.HorarioObservacion}»", $"/Horarios/Profesor/{u.Id}");
        TempData["SuccessMessage"] = "Listo: la UTP recibió tu comentario y revisará tu horario.";
        return RedirectToAction(nameof(Profesor), new { id = u.Id });
    }

    /// <summary>
    /// Docente con horario aprobado (bloqueado): pide un cambio a la UTP. Elige el bloque, a qué día/hora le acomoda y el motivo.
    /// No desbloquea nada: la UTP revisa si se puede y, si corresponde, desbloquea o hace el intercambio.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarCambio(string? bloque, string? prefiere, string? motivo)
    {
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == UsuarioId());
        if (u == null) return NotFound();
        motivo = (motivo ?? "").Trim();
        if (string.IsNullOrWhiteSpace(bloque) || motivo.Length < 3)
        {
            TempData["Error"] = "Elige el bloque que quieres cambiar y cuéntale a la UTP el motivo.";
            return RedirectToAction(nameof(Profesor), new { id = u.Id });
        }
        var texto = $"Pide cambiar: {bloque.Trim()}" + (string.IsNullOrWhiteSpace(prefiere) ? "" : $" · Le acomoda: {prefiere.Trim()}") + $" · Motivo: {motivo}";
        u.HorarioObservacion = texto.Length > 500 ? texto[..500] : texto;
        await _context.SaveChangesAsync();
        await AvisarUtpAsync(u.ColegioId, "📝 Solicitud de cambio de horario", $"{u.Nombre} {u.Apellido}: {u.HorarioObservacion}", $"/Horarios/Profesor/{u.Id}");
        TempData["SuccessMessage"] = "📨 Tu solicitud llegó a la UTP. Tu horario sigue igual hasta que la UTP lo revise.";
        return RedirectToAction(nameof(Profesor), new { id = u.Id });
    }

    /// <summary>La UTP da por resuelta la solicitud del docente (con o sin cambio).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverSolicitudDocente(int profesorId, string? respuesta)
    {
        if (!User.IsInRole("UTP")) return RedirectToAction("AccesoDenegado", "Account");
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == profesorId && x.ColegioId == ColegioId());
        if (u == null) return NotFound();
        u.HorarioObservacion = null;
        _context.Notificaciones.Add(new Notificacion
        {
            ColegioId = u.ColegioId, UsuarioId = u.Id, Titulo = "🗓️ La UTP respondió tu solicitud de horario",
            Mensaje = string.IsNullOrWhiteSpace(respuesta) ? "Tu solicitud fue revisada." : respuesta.Trim()[..Math.Min(respuesta.Trim().Length, 300)],
            Enlace = $"/Horarios/Profesor/{u.Id}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
        });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Respuesta enviada al profesor/a.";
        return RedirectToAction(nameof(Profesor), new { id = profesorId });
    }

    /// <summary>La UTP aprueba el horario confirmado por el docente (queda bloqueado) o lo desbloquea.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AprobarHorario(int profesorId, bool desbloquear = false)
    {
        // Bloquear y desbloquear horarios es solo de la UTP
        if (!User.IsInRole("UTP")) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == profesorId && x.ColegioId == colegioId);
        if (u == null) return NotFound();
        if (desbloquear)
        {
            u.HorarioBloqueado = false;
            u.HorarioAprobadoUtc = null;
            TempData["SuccessMessage"] = $"🔓 Horario de {u.Nombre} {u.Apellido} desbloqueado: ya se puede modificar. Cuando esté listo, que lo confirme de nuevo.";
        }
        else
        {
            var ultimo = await _context.ClasesHorario.Where(c => c.ProfesorId == profesorId).MaxAsync(c => (DateTime?)c.ModificadaUtc);
            if (u.HorarioConfirmadoUtc == null || (ultimo != null && u.HorarioConfirmadoUtc < ultimo))
            {
                TempData["Error"] = $"{u.Nombre} {u.Apellido} aún no confirma su horario (o cambió después de confirmarlo). Se bloquea cuando están de acuerdo los dos.";
                return RedirectToAction(nameof(Profesor), new { id = profesorId });
            }
            u.HorarioBloqueado = true;
            u.HorarioAprobadoUtc = DateTime.UtcNow;
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = colegioId, UsuarioId = u.Id, Titulo = "🔒 Tu horario quedó aprobado",
                Mensaje = "La UTP aprobó tu horario: queda fijo. Si necesitas un cambio, conversa con la UTP.",
                Enlace = $"/Horarios/Profesor/{u.Id}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
            TempData["SuccessMessage"] = $"🔒 Horario de {u.Nombre} {u.Apellido} aprobado y bloqueado. El automático ya no lo mueve.";
        }
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Profesor), new { id = profesorId });
    }

    /// <summary>Bloquea o desbloquea el horario completo de un curso.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BloquearCurso(int cursoId, bool bloquear)
    {
        if (!User.IsInRole("UTP")) return RedirectToAction("AccesoDenegado", "Account");
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        curso.HorarioBloqueado = bloquear;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = bloquear ? $"🔒 Horario de {curso.Nombre} {curso.Letra} bloqueado: ya no se mueve." : $"🔓 Horario de {curso.Nombre} {curso.Letra} desbloqueado.";
        return Redirect($"/Horarios/Curso/{cursoId}");
    }

    // ---------------------------------------------------------------- Mover o intercambiar bloques
    private string Cuando(int dia, BloqueJornada? b) => $"{Dias[dia - 1][..3].ToLower()} {b?.HoraInicio.ToString("hh\\:mm")}";

    /// <summary>¿Está libre este docente en ese día y bloque (sin contar las clases indicadas)?</summary>
    private Task<bool> LibreAsync(int? profesorId, int dia, int bloqueId, params int[] excepto) =>
        profesorId == null ? Task.FromResult(true)
        : _context.ClasesHorario.AllAsync(c => !(c.ProfesorId == profesorId && c.Dia == dia && c.BloqueJornadaId == bloqueId && !excepto.Contains(c.Id)));

    /// <summary>Aplica el movimiento: A pasa al destino; si había una clase B ahí, B pasa al lugar de A.</summary>
    private async Task AplicarMovimientoAsync(ClaseHorario a, ClaseHorario? b, int diaDestino, int bloqueDestinoId)
    {
        var (diaA, bloqueA) = (a.Dia, a.BloqueJornadaId);
        if (b == null)
        {
            a.Dia = diaDestino; a.BloqueJornadaId = bloqueDestinoId; a.ModificadaUtc = DateTime.UtcNow;
        }
        else
        {
            // Se intercambia el contenido (cada fila queda en su bloque): evita choques con el índice de curso/día/bloque
            (a.Asignatura, b.Asignatura) = (b.Asignatura, a.Asignatura);
            (a.ProfesorId, b.ProfesorId) = (b.ProfesorId, a.ProfesorId);
            (a.Sala, b.Sala) = (b.Sala, a.Sala);
            a.ModificadaUtc = b.ModificadaUtc = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// La UTP mueve una clase a otro bloque del mismo curso (o la intercambia con la que está ahí).
    /// Si la otra clase es de un docente con horario bloqueado, no se cambia directo: se le envía una solicitud
    /// y el cambio se hace solo cuando acepta. El docente ve el motivo real o solo «motivos personales», según elija la UTP.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Intercambiar(int cursoId, int diaOrigen, int bloqueOrigenId, int diaDestino, int bloqueDestinoId, string? motivo, bool mostrarMotivo = false)
    {
        if (!await PuedeEditarAsync()) return Json(new { ok = false, error = "No tienes permiso para modificar horarios." });
        var colegioId = ColegioId();
        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return Json(new { ok = false, error = "Curso no encontrado." });
        if (curso.HorarioBloqueado) return Json(new { ok = false, error = "🔒 El horario de este curso está bloqueado. Desbloquéalo para hacer cambios." });
        if (diaOrigen == diaDestino && bloqueOrigenId == bloqueDestinoId) return Json(new { ok = false, error = "Elige un bloque distinto." });
        var bloques = await JornadaAsync(colegioId);
        var bDest = bloques.FirstOrDefault(b => b.Id == bloqueDestinoId);
        var bOrig = bloques.FirstOrDefault(b => b.Id == bloqueOrigenId);
        if (bDest == null || bDest.Tipo != TipoBloque.Clase || diaDestino < 1 || diaDestino > 5) return Json(new { ok = false, error = "Ese bloque no es de clases." });
        var (salida, tarde) = ViernesDe(curso, await ViernesAsync(colegioId));
        if (diaDestino == 5 && !tarde && bDest.HoraInicio >= salida) return Json(new { ok = false, error = $"El viernes este curso sale a las {salida:hh\\:mm}." });

        var a = await _context.ClasesHorario.Include(c => c.Profesor).FirstOrDefaultAsync(c => c.CursoId == cursoId && c.Dia == diaOrigen && c.BloqueJornadaId == bloqueOrigenId);
        if (a == null) return Json(new { ok = false, error = "No hay clase en el bloque de origen." });
        var b = await _context.ClasesHorario.Include(c => c.Profesor).FirstOrDefaultAsync(c => c.CursoId == cursoId && c.Dia == diaDestino && c.BloqueJornadaId == bloqueDestinoId);

        // Sin choques: A debe estar libre en el destino y B (si existe) en el origen, en sus otros cursos
        if (!await LibreAsync(a.ProfesorId, diaDestino, bloqueDestinoId, a.Id, b?.Id ?? 0))
            return Json(new { ok = false, error = $"{a.Profesor?.Nombre} {a.Profesor?.Apellido} ya tiene clase en otro curso el {Cuando(diaDestino, bDest)}." });
        if (b != null && !await LibreAsync(b.ProfesorId, diaOrigen, bloqueOrigenId, a.Id, b.Id))
            return Json(new { ok = false, error = $"{b.Profesor?.Nombre} {b.Profesor?.Apellido} ya tiene clase en otro curso el {Cuando(diaOrigen, bOrig)}." });

        var resumen = $"{a.Asignatura} ({Cuando(diaOrigen, bOrig)})" + (b != null ? $" ↔ {b.Asignatura} ({Cuando(diaDestino, bDest)})" : $" → {Cuando(diaDestino, bDest)}") + $" · {curso.Nombre} {curso.Letra}".TrimEnd();
        motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()[..Math.Min(motivo.Trim().Length, 300)];

        // La otra clase es de un docente con horario bloqueado (y distinto al de A): se le pide permiso
        if (b?.Profesor != null && b.Profesor.HorarioBloqueado && b.ProfesorId != a.ProfesorId)
        {
            if (await _context.SolicitudesCambioHorario.AnyAsync(x => x.Estado == EstadoSolicitudHorario.Pendiente && (x.ClaseAId == a.Id || x.ClaseBId == b.Id)))
                return Json(new { ok = false, error = "Ya hay una solicitud pendiente para este bloque. Espera la respuesta o cancélala en Horarios del personal." });
            var sol = new SolicitudCambioHorario
            {
                ColegioId = colegioId, CursoId = cursoId, ClaseAId = a.Id, ClaseBId = b.Id, DiaDestino = diaDestino, BloqueDestinoId = bloqueDestinoId,
                ProfesorConsultadoId = b.ProfesorId!.Value, SolicitanteId = UsuarioId(), Resumen = resumen,
                MotivoMostrado = mostrarMotivo && motivo != null ? motivo : "Motivos personales de un colega",
                MotivoInterno = motivo
            };
            _context.SolicitudesCambioHorario.Add(sol);
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = colegioId, UsuarioId = b.ProfesorId.Value, Titulo = "🔁 ¿Puedes cambiar un bloque de tu horario?",
                Mensaje = $"La UTP te pide: {resumen}. Motivo: {sol.MotivoMostrado}. Responde si puedes o no.",
                Enlace = $"/Horarios/Profesor/{b.ProfesorId}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
            await _context.SaveChangesAsync();
            return Json(new { ok = true, pendiente = true, mensaje = $"📨 Se le preguntó a {b.Profesor.Nombre} {b.Profesor.Apellido} (su horario está bloqueado). El cambio se hace solo cuando acepte." });
        }

        await AplicarMovimientoAsync(a, b, diaDestino, bloqueDestinoId);
        // A cada docente afectado le llega el aviso (y su horario vuelve a quedar por confirmar)
        foreach (var pid in new[] { a.ProfesorId, b?.ProfesorId }.Where(x => x != null).Distinct())
        {
            if (pid == UsuarioId()) continue;
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = colegioId, UsuarioId = pid!.Value, Titulo = "🔁 Cambio en tu horario",
                Mensaje = $"La UTP movió: {resumen}." + (motivo != null && mostrarMotivo ? $" Motivo: {motivo}." : ""),
                Enlace = $"/Horarios/Profesor/{pid}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
        }
        await _context.SaveChangesAsync();
        return Json(new { ok = true, mensaje = "✅ Cambio hecho." });
    }

    /// <summary>El docente consultado acepta o rechaza el cambio (si rechaza, dice por qué).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResponderSolicitud(int id, bool acepta, string? respuesta)
    {
        var yo = UsuarioId();
        var sol = await _context.SolicitudesCambioHorario.FirstOrDefaultAsync(x => x.Id == id && x.ProfesorConsultadoId == yo && x.Estado == EstadoSolicitudHorario.Pendiente);
        if (sol == null) { TempData["Error"] = "Esa solicitud ya no está pendiente."; return RedirectToAction(nameof(Profesor), new { id = yo }); }
        respuesta = string.IsNullOrWhiteSpace(respuesta) ? null : respuesta.Trim()[..Math.Min(respuesta.Trim().Length, 300)];
        string aviso;
        if (acepta)
        {
            var a = await _context.ClasesHorario.FirstOrDefaultAsync(c => c.Id == sol.ClaseAId);
            var b = sol.ClaseBId != null ? await _context.ClasesHorario.FirstOrDefaultAsync(c => c.Id == sol.ClaseBId) : null;
            // Revisión de último minuto: que nada haya cambiado y que nadie quede en dos cursos a la vez
            var ok = a != null && (sol.ClaseBId == null || b != null)
                && await LibreAsync(a.ProfesorId, sol.DiaDestino, sol.BloqueDestinoId, a.Id, b?.Id ?? 0)
                && (b == null || await LibreAsync(b.ProfesorId, a.Dia, a.BloqueJornadaId, a.Id, b.Id));
            if (!ok)
            {
                sol.Estado = EstadoSolicitudHorario.Cancelada;
                sol.Respuesta = "El horario cambió mientras tanto: ya no se puede hacer este cambio.";
                sol.RespondidaUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["Error"] = "Gracias, pero el horario cambió mientras tanto y ya no se puede hacer este cambio. La UTP fue avisada.";
                aviso = $"No se pudo aplicar «{sol.Resumen}»: el horario cambió antes de la respuesta.";
            }
            else
            {
                await AplicarMovimientoAsync(a!, b, sol.DiaDestino, sol.BloqueDestinoId);
                sol.Estado = EstadoSolicitudHorario.Aceptada;
                sol.Respuesta = respuesta;
                sol.RespondidaUtc = DateTime.UtcNow;
                // Con el cambio aceptado, su horario sigue aprobado (lo aceptó él/ella)
                var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == yo);
                if (u != null && u.HorarioBloqueado) { u.HorarioConfirmadoUtc = DateTime.UtcNow; u.HorarioAprobadoUtc = DateTime.UtcNow; }
                TempData["SuccessMessage"] = "✅ ¡Gracias! El cambio ya quedó en tu horario.";
                aviso = $"{User.Identity?.Name} aceptó: {sol.Resumen}. Ya quedó en el horario.";
            }
        }
        else
        {
            if (respuesta == null) { TempData["Error"] = "Cuéntale a la UTP por qué no puedes (puedes elegir una de las opciones)."; return RedirectToAction(nameof(Profesor), new { id = yo }); }
            sol.Estado = EstadoSolicitudHorario.Rechazada;
            sol.Respuesta = respuesta;
            sol.RespondidaUtc = DateTime.UtcNow;
            TempData["SuccessMessage"] = "Listo: la UTP recibió tu respuesta.";
            aviso = $"{User.Identity?.Name} no puede: {sol.Resumen}. Motivo: {respuesta}";
        }
        _context.Notificaciones.Add(new Notificacion
        {
            ColegioId = sol.ColegioId, UsuarioId = sol.SolicitanteId, Titulo = acepta && sol.Estado == EstadoSolicitudHorario.Aceptada ? "✅ Cambio de horario aceptado" : "🔁 Respuesta a tu solicitud de horario",
            Mensaje = aviso, Enlace = $"/Horarios/Curso/{sol.CursoId}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
        });
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Profesor), new { id = yo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarSolicitud(int id)
    {
        if (!await PuedeEditarAsync()) return RedirectToAction("AccesoDenegado", "Account");
        var sol = await _context.SolicitudesCambioHorario.FirstOrDefaultAsync(x => x.Id == id && x.Estado == EstadoSolicitudHorario.Pendiente);
        if (sol != null) { sol.Estado = EstadoSolicitudHorario.Cancelada; sol.RespondidaUtc = DateTime.UtcNow; await _context.SaveChangesAsync(); }
        TempData["SuccessMessage"] = "Solicitud cancelada.";
        return RedirectToAction(nameof(Profesores));
    }

    private async Task AvisarUtpAsync(int colegioId, string titulo, string mensaje, string enlace)
    {
        var utps = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync();
        foreach (var id in utps)
            _context.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = id, Titulo = titulo, Mensaje = mensaje, Enlace = enlace, CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
        await _context.SaveChangesAsync();
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
        var horasProf = (await HorasProfesoresAsync(colegioId, id)).GetValueOrDefault(id);
        vm.Subtitulo = (u.Departamento ?? NombreRol(u.Rol)) + $" · {clases.Count} {(clases.Count == 1 ? "bloque" : "bloques")} de clase ({horasProf} horas pedagógicas) a la semana"
            + (u.HorasContrato is > 0 ? $" de {u.HorasContrato} por contrato" : "");
        vm.ProfesorId = u.Id;
        vm.EsPropio = u.Id == UsuarioId();
        vm.HorasContrato = u.HorasContrato;
        vm.Horas = horasProf;
        vm.ConfirmadoUtc = u.HorarioConfirmadoUtc;
        vm.UltimoCambioUtc = clases.Count == 0 ? null : clases.Max(c => c.ModificadaUtc);
        vm.ProfesorBloqueado = u.HorarioBloqueado;
        vm.AprobadoUtc = u.HorarioAprobadoUtc;
        vm.Observacion = u.HorarioObservacion;
        vm.PuedeAprobar = User.IsInRole("UTP");
        // Clubes a su cargo (van fuera del horario de clases)
        vm.ClubesFuera = await Auka.Web.Services.Clubes.DelDocenteAsync(_context, colegioId, id);
        // Directivos y personal sin clases: su jornada en el colegio (la arma el administrador) y si está en el colegio
        ViewBag.UsaJornada = JornadaPersonal.UsaJornada(u.Rol);
        ViewBag.Jornada = JornadaPersonal.Leer(u.JornadaPersonal);
        ViewBag.Estado = JornadaPersonal.Estado(u);
        ViewBag.PuedeJornada = EsAdministrador() && JornadaPersonal.UsaJornada(u.Rol);
        ViewBag.PuedePresencia = u.Id == UsuarioId() && EsAdministrador();
        vm.MisBloques = vm.EsPropio
            ? clases.OrderBy(c => c.Dia).ThenBy(c => vm.Bloques.FindIndex(b => b.Id == c.BloqueJornadaId))
                .Select(c => $"{Dias[c.Dia - 1]} {vm.Bloques.FirstOrDefault(b => b.Id == c.BloqueJornadaId)?.HoraInicio.ToString("hh\\:mm")} · {c.Asignatura} · {c.Curso?.Nombre} {c.Curso?.Letra}".Trim()).ToList()
            : new();
        // Solicitudes de cambio que le hicieron a este docente (pendientes arriba)
        vm.Solicitudes = await _context.SolicitudesCambioHorario.AsNoTracking()
            .Where(x => x.ProfesorConsultadoId == id && (x.Estado == EstadoSolicitudHorario.Pendiente || x.CreadaUtc >= DateTime.UtcNow.AddDays(-30)))
            .OrderBy(x => x.Estado).ThenByDescending(x => x.CreadaUtc).Take(10).ToListAsync();
        return View("Profesor", vm);
    }

    private bool EsAdministrador() => User.IsInRole("SuperAdmin") || User.IsInRole("Administrador");

    /// <summary>
    /// El administrador del colegio arma la jornada del director/a, la UTP, la suya y del resto del personal sin clases:
    /// entrada y salida de cada día (vacío = ese día no viene) y una nota opcional.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarJornadaPersonal(int usuarioId, string[]? entrada, string[]? salida, string? nota)
    {
        if (!EsAdministrador()) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.ColegioId == colegioId);
        if (u == null || !JornadaPersonal.UsaJornada(u.Rol)) return NotFound();
        var d = new JornadaPersonal.Datos { Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim()[..Math.Min(nota.Trim().Length, 200)] };
        for (var i = 0; i < 6; i++)
        {
            var e = entrada?.ElementAtOrDefault(i); var s = salida?.ElementAtOrDefault(i);
            if (TimeSpan.TryParse(e, out var te) && TimeSpan.TryParse(s, out var ts))
            {
                if (ts <= te) { TempData["Error"] = $"{JornadaPersonal.NombresDia[i + 1]}: la salida tiene que ser después de la entrada."; return RedirectToAction(nameof(Profesor), new { id = usuarioId }); }
                d.Dias.Add(new JornadaPersonal.Dia(i + 1, te.ToString("hh\\:mm"), ts.ToString("hh\\:mm")));
            }
        }
        u.JornadaPersonal = JornadaPersonal.Escribir(d);
        if (u.Id != UsuarioId())
            _context.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = u.Id, Titulo = "🕘 Tu jornada en el colegio se actualizó",
                Mensaje = "El administrador del colegio registró tu horario de entrada y salida.", Enlace = $"/Horarios/Profesor/{u.Id}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"🕘 Jornada de {u.Nombre} {u.Apellido} guardada.";
        return RedirectToAction(nameof(Profesor), new { id = usuarioId });
    }

    /// <summary>Botón del administrador: "Estoy en el colegio" / "No estoy". Vale por hoy.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Presencia(bool enColegio, string? volver)
    {
        if (!EsAdministrador()) return RedirectToAction("AccesoDenegado", "Account");
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == UsuarioId());
        if (u == null) return NotFound();
        u.EnColegio = enColegio; u.EnColegioUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["PanelOk"] = TempData["SuccessMessage"] = enColegio ? "🟢 Quedó marcado que estás en el colegio." : "⚪ Quedó marcado que hoy no estás en el colegio.";
        return LocalRedirect(Url.IsLocalUrl(volver) ? volver! : $"/Horarios/Profesor/{u.Id}");
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
        var profIds = clases.Where(c => c.ProfesorId != null).Select(c => c.ProfesorId!.Value).Distinct().ToList();
        var bloqueados = (await _context.Usuarios.AsNoTracking().Where(u => profIds.Contains(u.Id) && u.HorarioBloqueado).Select(u => u.Id).ToListAsync()).ToHashSet();
        foreach (var c in clases)
            vm.Celdas[(c.Dia, c.BloqueJornadaId)] = new CeldaVm(c.Asignatura,
                porProfesor ? $"{c.Curso?.Nombre} {c.Curso?.Letra}".Trim() : (c.Profesor != null ? $"{c.Profesor.Nombre} {c.Profesor.Apellido}" : null),
                c.Sala, c.ProfesorId, c.ProfesorId != null && bloqueados.Contains(c.ProfesorId.Value), c.CursoId);
        // Clases iniciadas hoy (todos las ven en el horario: UTP, inspectoría, alumnos/as y apoderados/as)
        vm.Iniciadas = (await ClasesEnCurso.DelDiaAsync(_context, colegioId, AsistenciaController.Hoy))
            .ToDictionary(x => x.Key, x => ClasesEnCurso.Texto(x.Value));
        return vm;
    }
}

public record CeldaVm(string Asignatura, string? Detalle, string? Sala, int? ProfesorId, bool Bloqueada = false, int? CursoId = null);

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
    // Viernes del curso
    public bool ViernesPropio { get; set; }
    public TimeSpan ViernesSalidaColegio { get; set; } = TimeSpan.FromHours(24);   // para marcar los bloques de recuperación
    public string ViernesColegio { get; set; } = "";
    // Horario del profesor/a
    public int? ProfesorId { get; set; }
    public int? ProfesorJefeId { get; set; }
    public bool EsPropio { get; set; }
    /// <summary>Hoy: (curso, bloque) → "🟢 Clase iniciada 09:47" / "✔️ Clase finalizada".</summary>
    public Dictionary<(int Curso, int Bloque), string> Iniciadas { get; set; } = new();
    public int? HorasContrato { get; set; }
    public int Horas { get; set; }
    public DateTime? ConfirmadoUtc { get; set; }
    public DateTime? UltimoCambioUtc { get; set; }
    // Bloqueo y acuerdos
    public bool CursoBloqueado { get; set; }
    public bool ProfesorBloqueado { get; set; }
    public DateTime? AprobadoUtc { get; set; }
    public string? Observacion { get; set; }
    public bool PuedeAprobar { get; set; }
    public List<string> MisBloques { get; set; } = new();
    // Clubes y talleres (fuera del horario de clases)
    public List<Auka.Web.Services.Clubes.FilaClub> ClubesFuera { get; set; } = new();
    public List<SolicitudCambioHorario> Solicitudes { get; set; } = new();
}
