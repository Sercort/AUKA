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

    /// <summary>Viernes efectivo de un curso: el propio si lo tiene, si no el del colegio.</summary>
    private static (TimeSpan Salida, bool Tarde) ViernesDe(Curso c, (TimeSpan Salida, bool Tarde) col) =>
        c.ViernesTarde == true ? (c.ViernesSalida ?? col.Salida, true)
        : c.ViernesSalida != null ? (c.ViernesSalida.Value, false)
        : col;

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
            var horas = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorId != null)
                .GroupBy(c => c.ProfesorId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
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

        var clase = await _context.ClasesHorario.FirstOrDefaultAsync(c => c.CursoId == cursoId && c.Dia == dia && c.BloqueJornadaId == bloqueId);
        var profAnterior = clase?.ProfesorId;
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
            var horas = await _context.ClasesHorario.CountAsync(c => c.ProfesorId == profesorId);
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
        var horas = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorId != null)
            .GroupBy(c => c.ProfesorId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
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
        if (o <= 1) return new() { ("Lenguaje y Comunicación", 8), ("Matemática", 6), ("Ciencias Naturales", 4), ("Educación Física y Salud", 4), ("Artes Visuales", 4), ("Música", 2), ("Orientación", 1) };
        if (o >= 2 && o <= 7) return new() { ("Lenguaje y Comunicación", 8), ("Matemática", 6), ("Historia, Geografía y Cs. Sociales", 3), ("Ciencias Naturales", 3), ("Inglés", 2), ("Educación Física y Salud", 4), ("Artes Visuales", 2), ("Música", 2), ("Tecnología", 1), ("Religión", 2), ("Orientación", 1) };
        if (o == 8 || o == 9) return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 6), ("Historia, Geografía y Cs. Sociales", 4), ("Ciencias Naturales", 4), ("Inglés", 3), ("Educación Física y Salud", 2), ("Artes Visuales", 2), ("Música", 2), ("Tecnología", 1), ("Religión", 2), ("Orientación", 1), ("Consejo de Curso", 1) };
        if (o == 10 || o == 11) return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 7), ("Historia, Geografía y Cs. Sociales", 4), ("Biología", 2), ("Química", 2), ("Física", 2), ("Inglés", 4), ("Educación Física y Salud", 2), ("Artes Visuales", 2), ("Tecnología", 2), ("Orientación", 1), ("Consejo de Curso", 1) };
        if (o >= 12 && !string.IsNullOrWhiteSpace(especialidad))
            return new() { ($"Especialidad TP - {especialidad}", 14), ("Lenguaje y Comunicación", 3), ("Matemática", 3), ("Inglés", 2), ("Educación Ciudadana", 2), ("Filosofía", 2), ("Educación Física y Salud", 2), ("Orientación", 1), ("Consejo de Curso", 1) };
        if (o >= 12) return new() { ("Lenguaje y Comunicación", 3), ("Matemática", 3), ("Educación Ciudadana", 2), ("Filosofía", 2), ("Ciencias para la Ciudadanía", 2), ("Inglés", 2), ("Educación Física y Salud", 2), ("Biología", 3), ("Química", 3), ("Física", 3), ("Historia, Geografía y Cs. Sociales", 3), ("Orientación", 1), ("Consejo de Curso", 1) };
        return new() { ("Lenguaje y Comunicación", 6), ("Matemática", 6), ("Orientación", 1) };
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
        cursos = cursos.OrderBy(c => Niveles.Orden(c.Nombre)).ThenBy(c => c.Letra).ToList();
        if (cursoId != null && rehacer)
        {
            _context.ClasesHorario.RemoveRange(await _context.ClasesHorario.Where(c => c.CursoId == cursoId).ToListAsync());
            await _context.SaveChangesAsync();
        }

        var existentes = await _context.ClasesHorario.Where(c => c.ColegioId == colegioId).ToListAsync();
        var cursoOcupado = existentes.Select(c => (c.CursoId, c.Dia, c.BloqueJornadaId)).ToHashSet();
        var profOcupado = existentes.Where(c => c.ProfesorId != null).Select(c => (c.ProfesorId!.Value, c.Dia, c.BloqueJornadaId)).ToHashSet();
        var carga = existentes.Where(c => c.ProfesorId != null).GroupBy(c => c.ProfesorId!.Value).ToDictionary(g => g.Key, g => g.Count());

        var docentes = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
            .Select(u => new { u.Id, u.Asignaturas, u.Departamento, u.HorasContrato }).ToListAsync();
        int Tope(int id) { var h = docentes.FirstOrDefault(d => d.Id == id)?.HorasContrato ?? 0; return h > 0 ? h : 38; }

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

            foreach (var (asig, horas) in PlanDe(curso.Nombre, especialidad))
            {
                // Profesor/a para esta asignatura en este curso (el mismo para todas sus horas, si se puede)
                List<int> candidatos;
                if (EsDeJefatura(asig)) candidatos = curso.ProfesorJefeId != null ? new List<int> { curso.ProfesorJefeId.Value } : new List<int>();
                else
                {
                    var depto = DeptoDe(asig);
                    var clave = asig.StartsWith("Especialidad TP - ") ? asig["Especialidad TP - ".Length..].ToLower() : asig.ToLower()[..Math.Min(asig.Length, 6)];
                    candidatos = docentes.Where(d =>
                            (d.Asignaturas != null && d.Asignaturas.ToLower().Contains(clave))
                            || (!asig.StartsWith("Especialidad") && depto != null && (d.Departamento == depto || DeptoDe(d.Asignaturas) == depto)))
                        .OrderBy(d => carga.GetValueOrDefault(d.Id)).Select(d => d.Id).ToList();
                }

                for (var h = 0; h < horas; h++)
                {
                    // Bloques libres del curso, primero los días con menos horas de esta asignatura y temprano
                    var libres = slots.Where(sl => !cursoOcupado.Contains((curso.Id, sl.Dia, sl.B.Id)))
                        .OrderBy(sl => porDia.GetValueOrDefault((asig, sl.Dia))).ThenBy(sl => sl.B.HoraInicio).ThenBy(sl => sl.Dia).ToList();
                    if (libres.Count == 0) { sinEspacio += horas - h; break; }

                    (int Dia, BloqueJornada B)? elegido = null; int? prof = null;
                    foreach (var sl in libres)
                    {
                        var p = candidatos.FirstOrDefault(c => !profOcupado.Contains((c, sl.Dia, sl.B.Id)) && carga.GetValueOrDefault(c) < Tope(c));
                        if (p != 0) { elegido = sl; prof = p; break; }
                    }
                    if (elegido == null) { elegido = libres[0]; prof = null; sinProfesor++; faltan[asig] = faltan.GetValueOrDefault(asig) + 1; }

                    var (dia, bl) = elegido.Value;
                    nuevos.Add(new ClaseHorario { ColegioId = colegioId, CursoId = curso.Id, Dia = dia, BloqueJornadaId = bl.Id, Asignatura = asig, ProfesorId = prof, ModificadaUtc = DateTime.UtcNow });
                    cursoOcupado.Add((curso.Id, dia, bl.Id));
                    porDia[(asig, dia)] = porDia.GetValueOrDefault((asig, dia)) + 1;
                    if (prof != null)
                    {
                        profOcupado.Add((prof.Value, dia, bl.Id));
                        carga[prof.Value] = carga.GetValueOrDefault(prof.Value) + 1;
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
        if (sinProfesor > 0) msg += $" {sinProfesor} quedaron sin profesor/a (falta personal o asignatura en Personal): " + string.Join(", ", faltan.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} ({x.Value})")) + ".";
        if (sinEspacio > 0) msg += $" {sinEspacio} horas no cupieron en la jornada.";
        msg += " Revísalo y ajústalo tocando cada bloque.";
        TempData["SuccessMessage"] = msg;
        return cursoId != null ? Redirect($"/Horarios/Curso/{cursoId}") : RedirectToAction(nameof(Index));
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
        var horas = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorId != null)
            .GroupBy(c => c.ProfesorId!.Value).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        ViewBag.Horas = horas.ToDictionary(h => h.Key, h => h.N);
        // Cursos de cada docente y en qué ciclo hace clases (básica / media)
        var cursosProf = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso)
            .Where(c => c.ColegioId == colegioId && c.ProfesorId != null)
            .Select(c => new { P = c.ProfesorId!.Value, c.Curso!.Nombre, c.Curso.Letra, c.ModificadaUtc }).ToListAsync();
        ViewBag.Cursos = cursosProf.GroupBy(x => x.P).ToDictionary(g => g.Key,
            g => g.Select(x => (x.Nombre + " " + x.Letra).Trim()).Distinct().OrderBy(n => Niveles.Orden(n)).ThenBy(n => n).ToList());
        ViewBag.Ciclos = cursosProf.GroupBy(x => x.P).ToDictionary(g => g.Key, g => g.Select(x => Niveles.Ciclo(x.Nombre)).Distinct().ToList());
        ViewBag.UltimoCambio = cursosProf.GroupBy(x => x.P).ToDictionary(g => g.Key, g => g.Max(x => x.ModificadaUtc));
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
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "¡Gracias! Quedó registrado que estás de acuerdo con tu horario.";
        return RedirectToAction(nameof(Profesor), new { id = u.Id });
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
        vm.Subtitulo = (u.Departamento ?? NombreRol(u.Rol)) + $" · {clases.Count} {(clases.Count == 1 ? "bloque" : "bloques")} de clase a la semana"
            + (u.HorasContrato is > 0 ? $" de {u.HorasContrato} por contrato" : "");
        vm.ProfesorId = u.Id;
        vm.EsPropio = u.Id == UsuarioId();
        vm.HorasContrato = u.HorasContrato;
        vm.Horas = clases.Count;
        vm.ConfirmadoUtc = u.HorarioConfirmadoUtc;
        vm.UltimoCambioUtc = clases.Count == 0 ? null : clases.Max(c => c.ModificadaUtc);
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
    // Viernes del curso
    public bool ViernesPropio { get; set; }
    public TimeSpan ViernesSalidaColegio { get; set; } = TimeSpan.FromHours(24);   // para marcar los bloques de recuperación
    public string ViernesColegio { get; set; } = "";
    // Horario del profesor/a
    public int? ProfesorId { get; set; }
    public int? ProfesorJefeId { get; set; }
    public bool EsPropio { get; set; }
    public int? HorasContrato { get; set; }
    public int Horas { get; set; }
    public DateTime? ConfirmadoUtc { get; set; }
    public DateTime? UltimoCambioUtc { get; set; }
}
