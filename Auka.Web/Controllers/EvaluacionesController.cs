using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Models;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Calendario de evaluaciones. Cada evaluación es también una columna del libro de notas (N1, N2...):
/// lo que se programa aquí aparece en el libro esperando sus notas.
/// Programan: la UTP (cualquier curso) y el docente (sus asignaturas). Ven: todo el personal; alumno/a y apoderado/a su curso.
/// </summary>
[Authorize]
public class EvaluacionesController : Controller
{
    private readonly ApplicationDbContext _context;
    public EvaluacionesController(ApplicationDbContext context) => _context = context;

    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }
    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }
    private bool EsUtp => User.IsInRole("UTP");
    private bool VeTodo => EsUtp || User.IsInRole("Director") || User.IsInRole("SuperAdmin") || User.IsInRole("Orientadora") || User.IsInRole("Inspector");

    public static readonly string[] Tipos = { "Prueba", "Trabajo", "Disertación", "Control", "Proyecto", "Guía evaluada", "Examen" };

    // ================================================================ Entrada
    [HttpGet]
    public async Task<IActionResult> Index(int? cursoId)
    {
        var yo = UsuarioId();
        // Alumno/a o apoderado/a: directo a su curso
        if (!cursoId.HasValue && User.IsInRole("Estudiante"))
            cursoId = await _context.Estudiantes.Where(e => e.UsuarioId == yo).Select(e => (int?)e.CursoId).FirstOrDefaultAsync();
        if (!cursoId.HasValue && User.IsInRole("Apoderado"))
            cursoId = await _context.EstudiantesApoderados.Where(x => x.Apoderado != null && x.Apoderado.UsuarioId == yo && x.Estudiante != null)
                .Select(x => (int?)x.Estudiante!.CursoId).FirstOrDefaultAsync();
        if (cursoId.HasValue) return await Curso(cursoId.Value);

        // Personal: carpetas de cursos (el docente ve los suyos)
        var anio = DateTime.Now.Year;
        var q = _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe).Where(c => c.AnioAcademico == anio && c.Nombre != "Pacientes");
        if (!VeTodo)
        {
            var mios = await _context.ClasesHorario.Where(c => c.ProfesorId == yo).Select(c => c.CursoId).Distinct().ToListAsync();
            q = q.Where(c => mios.Contains(c.Id) || c.ProfesorJefeId == yo);
        }
        var cursos = await q.ToListAsync();
        var ids = cursos.Select(c => c.Id).ToList();
        var hoy = DateTime.UtcNow.Date;
        var proximas = await _context.Evaluaciones.AsNoTracking().Where(e => ids.Contains(e.CursoId) && e.Fecha >= hoy && e.Fecha < hoy.AddDays(15))
            .GroupBy(e => e.CursoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var alumnos = await _context.Estudiantes.Where(e => ids.Contains(e.CursoId)).GroupBy(e => e.CursoId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

        ViewBag.Carpetas = new CarpetasVm
        {
            Clave = "evaluaciones",
            Cursos = cursos.Select(c => new CarpetaCurso(c.Id, c.Nombre, c.Letra, Url.Action("Index", new { cursoId = c.Id })!,
                alumnos.GetValueOrDefault(c.Id), c.CapacidadMaxima,
                c.ProfesorJefe != null ? $"{c.ProfesorJefe.Nombre} {c.ProfesorJefe.Apellido}" : "sin prof. jefe",
                proximas.TryGetValue(c.Id, out var n) ? $"📝 {n} en 15 días" : "📝 Sin pruebas próximas", null, null, null,
                n >= 6 ? "alerta" : null, n >= 6 ? "Semanas cargadas" : null)).ToList()
        };
        ViewBag.Semana = await _context.Evaluaciones.AsNoTracking().CountAsync(e => ids.Contains(e.CursoId) && e.Fecha >= hoy && e.Fecha < hoy.AddDays(7));
        ViewBag.EsUtp = EsUtp;
        return View("Cursos");
    }

    public sealed record ItemEval(int Id, DateTime Fecha, string Asignatura, string Titulo, string? Tipo, string? Contenido, bool PuedeEditar, int Notas);

    /// <summary>Calendario de un curso, por mes. Lo programado aparece también en el libro de notas.</summary>
    private async Task<IActionResult> Curso(int cursoId)
    {
        var curso = await _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe).FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        var yo = UsuarioId();
        var misRamos = await RamosQuePuedeAsync(cursoId);
        var lista = await _context.Evaluaciones.AsNoTracking().Include(e => e.Asignatura)
            .Where(e => e.CursoId == cursoId || (e.Asignatura != null && e.Asignatura.CursoId == cursoId))
            .OrderBy(e => e.Fecha).Select(e => new { e.Id, e.Fecha, Ramo = e.Asignatura != null ? e.Asignatura.Nombre : "", e.Titulo, e.Descripcion, e.Contenido, N = e.Calificaciones.Count })
            .ToListAsync();
        ViewBag.Curso = curso;
        ViewBag.Ramos = misRamos;
        ViewBag.Tipos = Tipos;
        ViewBag.PuedeProgramar = misRamos.Count > 0;
        return View("Index", lista.Select(e => new ItemEval(e.Id, e.Fecha, e.Ramo, e.Titulo, e.Descripcion, e.Contenido,
            misRamos.Any(r => Csv.Clave(r) == Csv.Clave(e.Ramo)), e.N)).ToList());
    }

    /// <summary>Ramos del curso que esta persona puede programar: la UTP todos; el docente los que hace en ese curso.</summary>
    private async Task<List<string>> RamosQuePuedeAsync(int cursoId)
    {
        if (!EsUtp && !User.IsInRole("Docente")) return new();
        var yo = UsuarioId();
        var clases = await _context.ClasesHorario.AsNoTracking().Where(c => c.CursoId == cursoId && (EsUtp || c.ProfesorId == yo)).Select(c => c.Asignatura).ToListAsync();
        var ramos = clases.Where(a => !string.IsNullOrWhiteSpace(a) && !HorariosController.EsDeJefatura(a)).Select(a => a.Trim()).ToList();
        if (EsUtp) ramos.AddRange(await _context.Asignaturas.AsNoTracking().Where(a => a.CursoId == cursoId).Select(a => a.Nombre).ToListAsync());
        return ramos.GroupBy(Csv.Clave).Select(g => g.First()).OrderBy(x => x).ToList();
    }

    /// <summary>Busca (o crea) la asignatura del curso: el docente sale del horario.</summary>
    private async Task<Asignatura> AsignaturaAsync(Curso curso, string ramo)
    {
        var k = Csv.Clave(ramo);
        var asig = (await _context.Asignaturas.Where(a => a.CursoId == curso.Id).ToListAsync()).FirstOrDefault(a => Csv.Clave(a.Nombre) == k);
        if (asig != null) return asig;
        var prof = (await _context.ClasesHorario.Where(x => x.CursoId == curso.Id && x.ProfesorId != null).Select(x => new { x.Asignatura, x.ProfesorId }).ToListAsync())
            .Where(x => Csv.Clave(x.Asignatura) == k).GroupBy(x => x.ProfesorId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        asig = new Asignatura { Nombre = ramo.Trim(), Codigo = "", CursoId = curso.Id, DocenteId = prof ?? curso.ProfesorJefeId ?? UsuarioId() };
        _context.Asignaturas.Add(asig);
        await _context.SaveChangesAsync();
        return asig;
    }

    // ================================================================ Programar / quitar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Programar(int cursoId, string? ramo, string? titulo, DateOnly? fecha, string? tipo, string? contenido)
    {
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        var permitidos = await RamosQuePuedeAsync(cursoId);
        if (string.IsNullOrWhiteSpace(ramo) || !permitidos.Any(r => Csv.Clave(r) == Csv.Clave(ramo))) return RedirectToAction("AccesoDenegado", "Account");
        if (fecha == null || string.IsNullOrWhiteSpace(titulo)) { TempData["Error"] = "Falta la fecha o el nombre de la evaluación."; return RedirectToAction(nameof(Index), new { cursoId }); }
        var asig = await AsignaturaAsync(curso, ramo);
        _context.Evaluaciones.Add(new Evaluacion
        {
            Titulo = titulo.Trim()[..Math.Min(titulo.Trim().Length, 60)], AsignaturaId = asig.Id, CursoId = cursoId,
            Fecha = DateTime.SpecifyKind(fecha.Value.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc),
            Descripcion = Tipos.Contains(tipo) ? tipo : null, Contenido = string.IsNullOrWhiteSpace(contenido) ? null : contenido.Trim()[..Math.Min(contenido.Trim().Length, 500)]
        });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"📝 {titulo} de {ramo} programada para el {fecha:dd-MM-yyyy}. Ya aparece también en el libro de notas.";
        return RedirectToAction(nameof(Index), new { cursoId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quitar(int id)
    {
        var ev = await _context.Evaluaciones.Include(e => e.Asignatura).Include(e => e.Calificaciones).FirstOrDefaultAsync(e => e.Id == id);
        if (ev?.Asignatura == null) return NotFound();
        var cursoId = ev.Asignatura.CursoId;
        if (!(await RamosQuePuedeAsync(cursoId)).Any(r => Csv.Clave(r) == Csv.Clave(ev.Asignatura.Nombre))) return RedirectToAction("AccesoDenegado", "Account");
        if (ev.Calificaciones.Count > 0) { TempData["Error"] = "Esta evaluación ya tiene notas: no se puede quitar desde el calendario (hazlo en el libro de notas)."; return RedirectToAction(nameof(Index), new { cursoId }); }
        _context.Evaluaciones.Remove(ev);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Evaluación quitada del calendario.";
        return RedirectToAction(nameof(Index), new { cursoId });
    }

    // ================================================================ Exportar / importar
    /// <summary>Planilla del calendario: un curso, o todos los cursos que ve esta persona.</summary>
    [HttpGet]
    public async Task<IActionResult> Exportar(int? cursoId)
    {
        var anio = DateTime.Now.Year;
        var q = _context.Evaluaciones.AsNoTracking().Include(e => e.Asignatura).Include(e => e.Curso).Include(e => e.Calificaciones)
            .Where(e => e.Curso != null && e.Curso.AnioAcademico == anio);
        if (cursoId != null) q = q.Where(e => e.CursoId == cursoId);
        else if (!VeTodo) return RedirectToAction(nameof(Index));
        var lista = await q.OrderBy(e => e.Curso!.Nombre).ThenBy(e => e.Curso!.Letra).ThenBy(e => e.Fecha).ToListAsync();
        var filas = new List<List<string?>> { new() { "Nivel", "Letra", "Asignatura", "Evaluación", "Fecha", "Tipo", "Contenidos", "Notas registradas" } };
        filas.AddRange(lista.OrderBy(e => Niveles.Orden(e.Curso!.Nombre)).ThenBy(e => e.Curso!.Letra).ThenBy(e => e.Fecha)
            .Select(e => new List<string?> { e.Curso!.Nombre, e.Curso.Letra, e.Asignatura?.Nombre, e.Titulo, e.Fecha.ToString("dd-MM-yyyy"), e.Descripcion, e.Contenido, e.Calificaciones.Count.ToString() }));
        return File(Csv.Escribir(filas), "text/csv", cursoId != null ? "calendario_evaluaciones_curso.csv" : "calendario_evaluaciones_todos.csv");
    }

    /// <summary>
    /// Importa el calendario (UTP). Columnas: Nivel; Letra; Asignatura; Evaluación; Fecha; Tipo; Contenidos.
    /// Si esa evaluación ya existe en el curso y asignatura (mismo nombre), se actualiza la fecha y los contenidos: no se duplica.
    /// </summary>
    [Authorize(Roles = "UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Importar(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var pl = await Csv.LeerAsync(archivo);
        int iNiv = pl.Col("nivel", "curso"), iLet = pl.Col("letra"), iAsig = pl.Col("asignatura", "ramo"), iEv = pl.Col("evaluacion", "titulo", "nombre"),
            iFec = pl.Col("fecha"), iTipo = pl.Col("tipo"), iCont = pl.Col("contenido");
        if (iNiv < 0 || iAsig < 0 || iEv < 0 || iFec < 0) { TempData["Error"] = "Faltan columnas. Usa la plantilla: Nivel, Letra, Asignatura, Evaluación y Fecha."; return RedirectToAction(nameof(Index)); }

        var anio = DateTime.Now.Year;
        var cursos = await _context.Cursos.Where(c => c.AnioAcademico == anio).ToListAsync();
        var asigCache = new Dictionary<string, Asignatura>();
        var evCache = new Dictionary<int, List<Evaluacion>>();
        int nuevas = 0, actualizadas = 0;
        var errores = new List<string>();
        for (var i = 0; i < pl.Filas.Count; i++)
        {
            var f = pl.Filas[i]; var fila = i + 2;
            string C(int k) => Csv.Planilla.Celda(f, k);
            if (C(iNiv).Length == 0 && C(iEv).Length == 0) continue;
            var nivel = C(iNiv); var letra = iLet >= 0 ? C(iLet).ToUpperInvariant() : "";
            var curso = cursos.FirstOrDefault(c => Niveles.Normal(c.Nombre) == Niveles.Normal(nivel) && (letra.Length == 0 || (c.Letra ?? "").Trim().ToUpperInvariant() == letra));
            if (curso == null) { errores.Add($"Fila {fila}: no existe el curso {nivel} {letra} este año."); continue; }
            var fecha = Csv.Fecha(C(iFec));
            if (fecha == null) { errores.Add($"Fila {fila}: fecha no válida ({C(iFec)}); usa dd-mm-aaaa."); continue; }
            var ramo = C(iAsig); var titulo = C(iEv);
            if (ramo.Length == 0 || titulo.Length == 0) { errores.Add($"Fila {fila}: falta la asignatura o el nombre de la evaluación."); continue; }
            if (titulo.Length > 60) titulo = titulo[..60];
            var kA = $"{curso.Id}|{Csv.Clave(ramo)}";
            if (!asigCache.TryGetValue(kA, out var asig)) asigCache[kA] = asig = await AsignaturaAsync(curso, ramo);
            if (!evCache.TryGetValue(asig.Id, out var delRamo)) evCache[asig.Id] = delRamo = await _context.Evaluaciones.Where(e => e.AsignaturaId == asig.Id).ToListAsync();
            var ev = delRamo.FirstOrDefault(e => Csv.Clave(e.Titulo) == Csv.Clave(titulo));
            var tipo = Tipos.FirstOrDefault(t => Csv.Clave(t) == Csv.Clave(C(iTipo)));
            var cont = iCont >= 0 && C(iCont).Length > 0 ? C(iCont)[..Math.Min(C(iCont).Length, 500)] : null;
            if (ev == null)
            {
                var nueva = new Evaluacion { Titulo = titulo, AsignaturaId = asig.Id, CursoId = curso.Id, Fecha = fecha.Value, Descripcion = tipo, Contenido = cont };
                _context.Evaluaciones.Add(nueva);
                delRamo.Add(nueva);
                nuevas++;
            }
            else
            {
                ev.Fecha = fecha.Value; if (tipo != null) ev.Descripcion = tipo; if (cont != null) ev.Contenido = cont;
                actualizadas++;
            }
            if ((nuevas + actualizadas) % 300 == 0) await _context.SaveChangesAsync();
        }
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"📥 Calendario: {nuevas} evaluaciones nuevas, {actualizadas} actualizadas. También quedaron como columnas en el libro de notas.";
        if (errores.Count > 0) TempData["Error"] = $"{errores.Count} filas no se importaron: " + string.Join(" ", errores.Take(8)) + (errores.Count > 8 ? " …" : "");
        return RedirectToAction(nameof(Index));
    }
}
