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
/// Libro de notas digital.
/// Orden: nivel › curso › asignatura; adentro, cada alumno/a con sus notas en horizontal (N1, N2, ...) y su promedio.
/// Modelo usado (tablas que ya existían, sin migración):
///   Asignatura  = un ramo de un curso (se crea sola desde el horario o al importar).
///   Evaluacion  = una columna del libro (N1, N2, ...).
///   Calificacion = la nota de un alumno/a en esa evaluación.
/// Permisos: el docente pone y edita las notas de los ramos que hace; la UTP puede corregirlas (rectificación).
/// Cambiar una nota ya puesta exige motivo y avisa a la otra parte (UTP ↔ profesor/a), nunca al alumno/a ni al apoderado/a.
/// Director/a, administrador/a y orientador/a solo miran; el inspector no entra; alumno/a y apoderado/a ven solo lo suyo.
/// </summary>
[Authorize]
public class NotasController : Controller
{
    private readonly ApplicationDbContext _context;
    public NotasController(ApplicationDbContext context) => _context = context;

    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }
    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }
    // Quién ve todos los cursos (solo lectura salvo la UTP): UTP, director/a, administrador/a y orientador/a.
    private bool VeTodo => User.IsInRole("UTP") || User.IsInRole("Director") || User.IsInRole("SuperAdmin") || User.IsInRole("Administrador") || User.IsInRole("Orientadora");
    // La nota es del profesor/a de la asignatura; la UTP es la única que además puede corregirla (rectificación, con motivo).
    // Director/a y administrador/a supervisan: ven, no editan.
    private bool EsUtp => User.IsInRole("UTP");
    private bool EsDocente => User.IsInRole("Docente");

    /// <summary>Ramos que no llevan nota (son de jefatura).</summary>
    private static bool SinNota(string a) => HorariosController.EsDeJefatura(a);

    // ================================================================ Entrada
    [HttpGet]
    public async Task<IActionResult> Index(int? asignaturaId)
    {
        if (User.IsInRole("Inspector"))
        {
            TempData["Error"] = "🚫 El perfil Inspector no tiene acceso al libro de notas.";
            return RedirectToAction("Index", "Home");
        }
        if (User.IsInRole("Estudiante") || User.IsInRole("Apoderado")) return await MisNotas(asignaturaId);

        // Personal: carpetas de cursos (el docente ve solo los cursos donde hace clases o es profesor/a jefe)
        var anio = DateTime.Now.Year;
        var yo = UsuarioId();
        var q = _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe)
            .Where(c => c.AnioAcademico == anio && c.Nombre != "Pacientes");
        if (!VeTodo)
        {
            var mios = await _context.ClasesHorario.Where(c => c.ProfesorId == yo).Select(c => c.CursoId).Distinct().ToListAsync();
            q = q.Where(c => mios.Contains(c.Id) || c.ProfesorJefeId == yo);
        }
        var cursos = await q.ToListAsync();
        var ids = cursos.Select(c => c.Id).ToList();
        var alumnos = await _context.Estudiantes.Where(e => ids.Contains(e.CursoId)).GroupBy(e => e.CursoId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

        // Promedio de cada curso (todas sus notas) y cantidad de notas rojas
        var notas = await _context.Calificaciones.AsNoTracking()
            .Where(c => c.Asignatura != null && ids.Contains(c.Asignatura.CursoId))
            .Select(c => new { c.Asignatura!.CursoId, c.Nota }).ToListAsync();
        var resumen = notas.GroupBy(n => n.CursoId).ToDictionary(g => g.Key, g => (Prom: Math.Round(g.Average(x => x.Nota), 1, MidpointRounding.AwayFromZero), Rojas: g.Count(x => x.Nota < 4.0m)));

        ViewBag.Carpetas = new CarpetasVm
        {
            Clave = "notas",
            Cursos = cursos.Select(c =>
            {
                var r = resumen.TryGetValue(c.Id, out var x) ? x : (Prom: 0m, Rojas: 0);
                return new CarpetaCurso(c.Id, c.Nombre, c.Letra, Url.Action("Libro", new { cursoId = c.Id })!,
                    alumnos.GetValueOrDefault(c.Id), c.CapacidadMaxima,
                    c.ProfesorJefe != null ? $"{c.ProfesorJefe.Nombre} {c.ProfesorJefe.Apellido}" : "sin prof. jefe",
                    r.Prom > 0 ? "📊 Promedio " + Csv.Nota1(r.Prom) : "📊 Sin notas",
                    r.Rojas > 0 ? "🔴 " + r.Rojas + " rojas" : null,
                    null, null,
                    r.Prom > 0 && r.Prom < 4.5m ? "alerta" : null,
                    r.Prom > 0 && r.Prom < 4.5m ? "Promedio bajo" : null);
            }).ToList()
        };
        ViewBag.Total = notas.Count;
        ViewBag.Promedio = notas.Count > 0 ? Math.Round(notas.Average(n => n.Nota), 1, MidpointRounding.AwayFromZero) : 0m;
        ViewBag.Rojas = notas.Count(n => n.Nota < 4.0m);
        ViewBag.EsGestion = EsUtp;   // importar notas: solo UTP
        return View("Cursos");
    }

    /// <summary>Alumno/a o apoderado/a: solo sus notas (o las de sus pupilos).</summary>
    private async Task<IActionResult> MisNotas(int? asignaturaId)
    {
        var yo = UsuarioId();
        var q = _context.Calificaciones
            .Include(c => c.Estudiante).ThenInclude(e => e!.Usuario)
            .Include(c => c.Asignatura).ThenInclude(a => a!.Curso)
            .Include(c => c.Evaluacion)
            .AsQueryable();
        if (User.IsInRole("Estudiante"))
            q = q.Where(c => c.Estudiante != null && c.Estudiante.UsuarioId == yo);
        else
        {
            var apo = await _context.Apoderados.FirstOrDefaultAsync(a => a.UsuarioId == yo);
            var hijos = apo == null ? new List<int>() : await _context.EstudiantesApoderados.Where(x => x.ApoderadoId == apo.Id).Select(x => x.EstudianteId).ToListAsync();
            q = q.Where(c => hijos.Contains(c.EstudianteId));
        }
        if (asignaturaId > 0) q = q.Where(c => c.AsignaturaId == asignaturaId);
        return View("Index", await q.OrderByDescending(c => c.Fecha).ToListAsync());
    }

    // ================================================================ Libro del curso
    public sealed record Columna(int Id, string Titulo, DateTime Fecha, decimal Ponderacion);
    public sealed record FilaAlumno(int EstudianteId, string Nombre, string Rut, Dictionary<int, decimal> Notas, decimal? Promedio);
    public sealed record RamoVm(string Nombre, string? Profesor, decimal? Promedio, int Rojas, bool PuedeEditar);
    public sealed class LibroVm
    {
        public Curso Curso { get; set; } = null!;
        public List<RamoVm> Ramos { get; set; } = new();
        public string? Ramo { get; set; }
        public List<Columna> Columnas { get; set; } = new();
        public List<FilaAlumno> Filas { get; set; } = new();
        public bool PuedeEditar { get; set; }
        public bool Editando { get; set; }
        public decimal? PromedioRamo { get; set; }
        /// <summary>Vista "Resumen": promedio de cada alumno/a en cada ramo.</summary>
        public Dictionary<int, Dictionary<string, decimal>> Resumen { get; set; } = new();
    }

    /// <summary>Nombres de los ramos del curso: los del horario + los que ya tienen notas (sin repetir y sin jefatura).</summary>
    private async Task<List<(string Nombre, int? ProfesorId)>> RamosDelCursoAsync(int cursoId)
    {
        var delHorario = await _context.ClasesHorario.AsNoTracking().Where(c => c.CursoId == cursoId)
            .Select(c => new { c.Asignatura, c.ProfesorId }).ToListAsync();
        var conNotas = await _context.Asignaturas.AsNoTracking().Where(a => a.CursoId == cursoId).Select(a => new { a.Nombre, a.DocenteId }).ToListAsync();
        var res = new Dictionary<string, (string, int?)>();
        foreach (var g in delHorario.Where(c => !string.IsNullOrWhiteSpace(c.Asignatura) && !SinNota(c.Asignatura)).GroupBy(c => Csv.Clave(c.Asignatura)))
            res[g.Key] = (g.First().Asignatura.Trim(), g.Where(x => x.ProfesorId != null).GroupBy(x => x.ProfesorId).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault());
        foreach (var a in conNotas)
            if (!res.ContainsKey(Csv.Clave(a.Nombre))) res[Csv.Clave(a.Nombre)] = (a.Nombre, a.DocenteId);
        return res.Values.OrderBy(r => r.Item1).ToList();
    }

    private async Task<bool> PuedeEditarRamoAsync(int cursoId, string ramo)
    {
        if (EsUtp) return true;
        if (!EsDocente) return false;
        var yo = UsuarioId();
        var clave = Csv.Clave(ramo);
        var mias = await _context.ClasesHorario.Where(c => c.CursoId == cursoId && c.ProfesorId == yo).Select(c => c.Asignatura).ToListAsync();
        return mias.Any(a => Csv.Clave(a) == clave);
    }

    private async Task<bool> PuedeVerCursoAsync(Curso curso)
    {
        if (VeTodo) return true;
        if (!EsDocente) return false;
        var yo = UsuarioId();
        return curso.ProfesorJefeId == yo || await _context.ClasesHorario.AnyAsync(c => c.CursoId == curso.Id && c.ProfesorId == yo);
    }

    private static decimal? Promedio(IEnumerable<(decimal Nota, decimal Pond)> notas)
    {
        var l = notas.ToList();
        if (l.Count == 0) return null;
        var peso = l.Sum(x => x.Pond > 0 ? x.Pond : 1m);
        return Math.Round(l.Sum(x => x.Nota * (x.Pond > 0 ? x.Pond : 1m)) / peso, 1, MidpointRounding.AwayFromZero);
    }

    [HttpGet]
    public async Task<IActionResult> Libro(int cursoId, string? ramo, bool editar = false)
    {
        if (User.IsInRole("Inspector") || User.IsInRole("Estudiante") || User.IsInRole("Apoderado")) return RedirectToAction(nameof(Index));
        var curso = await _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe).FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        if (!await PuedeVerCursoAsync(curso)) return RedirectToAction("AccesoDenegado", "Account");

        var yo = UsuarioId();
        var ramos = await RamosDelCursoAsync(cursoId);
        var profIds = ramos.Where(r => r.ProfesorId != null).Select(r => r.ProfesorId!.Value).Distinct().ToList();
        var profes = await _context.Usuarios.AsNoTracking().Where(u => profIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre + " " + u.Apellido);

        var alumnos = await _context.Estudiantes.AsNoTracking().Include(e => e.Usuario)
            .Where(e => e.CursoId == cursoId && e.Usuario != null && e.Usuario.Activo)
            .ToListAsync();
        alumnos = alumnos.OrderBy(e => e.Usuario!.Apellido).ThenBy(e => e.Usuario!.Nombre).ToList();

        // Todas las notas del curso (para promedios por ramo y la vista resumen)
        var asigs = await _context.Asignaturas.AsNoTracking().Where(a => a.CursoId == cursoId).ToListAsync();
        var asigIds = asigs.Select(a => a.Id).ToList();
        var todas = await _context.Calificaciones.AsNoTracking()
            .Where(c => asigIds.Contains(c.AsignaturaId))
            .Select(c => new { c.EstudianteId, c.AsignaturaId, c.EvaluacionId, c.Nota, Pond = c.Evaluacion != null ? c.Evaluacion.Ponderacion : 1m })
            .ToListAsync();
        var claveDeAsig = asigs.ToDictionary(a => a.Id, a => Csv.Clave(a.Nombre));

        // El docente que no es jefe ve solo sus ramos
        var esJefe = curso.ProfesorJefeId == yo;
        var visibles = new List<RamoVm>();
        foreach (var r in ramos)
        {
            var puede = await PuedeEditarRamoAsync(cursoId, r.Nombre);
            if (!VeTodo && !esJefe && !puede) continue;
            var k = Csv.Clave(r.Nombre);
            var deRamo = todas.Where(t => claveDeAsig.GetValueOrDefault(t.AsignaturaId) == k).ToList();
            var porAlumno = deRamo.GroupBy(t => t.EstudianteId).Select(g => Promedio(g.Select(x => (x.Nota, x.Pond)))!.Value).ToList();
            visibles.Add(new RamoVm(r.Nombre, r.ProfesorId != null && profes.TryGetValue(r.ProfesorId.Value, out var p) ? p : null,
                porAlumno.Count > 0 ? Math.Round(porAlumno.Average(), 1, MidpointRounding.AwayFromZero) : null,
                porAlumno.Count(x => x < 4.0m), puede));
        }

        var vm = new LibroVm { Curso = curso, Ramos = visibles };
        var elegido = visibles.FirstOrDefault(r => Csv.Clave(r.Nombre) == Csv.Clave(ramo));
        if (elegido == null)
        {
            // Sin ramo elegido: resumen con el promedio de cada alumno/a en cada ramo
            foreach (var a in alumnos)
            {
                var d = new Dictionary<string, decimal>();
                foreach (var r in visibles)
                {
                    var k = Csv.Clave(r.Nombre);
                    var p = Promedio(todas.Where(t => t.EstudianteId == a.Id && claveDeAsig.GetValueOrDefault(t.AsignaturaId) == k).Select(t => (t.Nota, t.Pond)));
                    if (p != null) d[r.Nombre] = p.Value;
                }
                vm.Resumen[a.Id] = d;
            }
            vm.Filas = alumnos.Select(a => new FilaAlumno(a.Id, $"{a.Usuario!.Apellido}, {a.Usuario.Nombre}", a.Usuario.Rut, new(),
                vm.Resumen[a.Id].Count > 0 ? Math.Round(vm.Resumen[a.Id].Values.Average(), 1, MidpointRounding.AwayFromZero) : null)).ToList();
            return View(vm);
        }

        vm.Ramo = elegido.Nombre;
        vm.PuedeEditar = elegido.PuedeEditar;
        vm.Editando = editar && elegido.PuedeEditar;
        var asig = asigs.FirstOrDefault(a => Csv.Clave(a.Nombre) == Csv.Clave(elegido.Nombre));
        if (asig != null)
        {
            vm.Columnas = await _context.Evaluaciones.AsNoTracking().Where(e => e.AsignaturaId == asig.Id)
                .OrderBy(e => e.Fecha).ThenBy(e => e.Id)
                .Select(e => new Columna(e.Id, e.Titulo, e.Fecha, e.Ponderacion)).ToListAsync();
        }
        var notasRamo = todas.Where(t => asig != null && t.AsignaturaId == asig.Id).ToList();
        vm.Filas = alumnos.Select(a =>
        {
            var mias = notasRamo.Where(n => n.EstudianteId == a.Id).ToList();
            return new FilaAlumno(a.Id, $"{a.Usuario!.Apellido}, {a.Usuario.Nombre}", a.Usuario.Rut,
                mias.GroupBy(n => n.EvaluacionId).ToDictionary(g => g.Key, g => g.First().Nota),
                Promedio(mias.Select(n => (n.Nota, n.Pond))));
        }).ToList();
        vm.PromedioRamo = elegido.Promedio;
        return View(vm);
    }

    /// <summary>Busca (o crea) el ramo de un curso para guardar notas en él.</summary>
    private async Task<Asignatura> AsignaturaAsync(Curso curso, string ramo, Dictionary<string, Asignatura>? cache = null)
    {
        var k = Csv.Clave(ramo);
        if (cache != null && cache.TryGetValue($"{curso.Id}|{k}", out var c)) return c;
        var lista = await _context.Asignaturas.Where(a => a.CursoId == curso.Id).ToListAsync();
        var asig = lista.FirstOrDefault(a => Csv.Clave(a.Nombre) == k);
        if (asig == null)
        {
            // El docente del ramo sale del horario; si no hay, queda quien registra
            var nombres = await _context.ClasesHorario.Where(x => x.CursoId == curso.Id && x.ProfesorId != null)
                .Select(x => new { x.Asignatura, x.ProfesorId }).ToListAsync();
            var prof = nombres.Where(x => Csv.Clave(x.Asignatura) == k).GroupBy(x => x.ProfesorId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            asig = new Asignatura { Nombre = ramo.Trim(), Codigo = "", CursoId = curso.Id, DocenteId = prof ?? curso.ProfesorJefeId ?? UsuarioId() };
            _context.Asignaturas.Add(asig);
            await _context.SaveChangesAsync();
        }
        if (cache != null) cache[$"{curso.Id}|{k}"] = asig;
        return asig;
    }

    /// <summary>Guarda el libro de un ramo: títulos de columnas, notas de cada alumno/a y una columna nueva opcional.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarLibro(int cursoId, string ramo)
    {
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        if (!await PuedeEditarRamoAsync(cursoId, ramo)) return RedirectToAction("AccesoDenegado", "Account");

        var asig = await AsignaturaAsync(curso, ramo);
        var evals = await _context.Evaluaciones.Where(e => e.AsignaturaId == asig.Id).ToListAsync();
        var evalIds = evals.Select(e => e.Id).ToList();
        var existentes = await _context.Calificaciones.Where(c => evalIds.Contains(c.EvaluacionId)).ToListAsync();
        var nombres = await _context.Estudiantes.Where(e => e.CursoId == cursoId && e.Usuario != null)
            .Select(e => new { e.Id, N = e.Usuario!.Nombre + " " + e.Usuario.Apellido }).ToDictionaryAsync(x => x.Id, x => x.N);
        var alumnos = nombres.Keys.ToList();
        var form = Request.Form;
        int cambios = 0, invalidas = 0;

        // Antes de guardar: ¿se cambia o borra alguna nota que ya estaba puesta? Eso exige motivo y genera aviso.
        var modificadas = new List<string>();
        foreach (var ev in evals)
        {
            var deCol = existentes.Where(c => c.EvaluacionId == ev.Id).ToList();
            if (form[$"del_{ev.Id}"] == "1")
            {
                if (deCol.Count > 0) modificadas.Add($"se quitó la evaluación {ev.Titulo} ({deCol.Count} notas)");
                continue;
            }
            foreach (var cal in deCol)
            {
                var txt = form[$"n_{cal.EstudianteId}_{ev.Id}"].ToString();
                var n = Csv.Nota(txt);
                var quien = nombres.GetValueOrDefault(cal.EstudianteId, "alumno/a");
                if (string.IsNullOrWhiteSpace(txt)) modificadas.Add($"{quien} {ev.Titulo}: {Csv.Nota1(cal.Nota)} → borrada");
                else if (n != null && n.Value != cal.Nota) modificadas.Add($"{quien} {ev.Titulo}: {Csv.Nota1(cal.Nota)} → {Csv.Nota1(n.Value)}");
            }
        }
        var motivo = form["motivo"].ToString().Trim();
        if (modificadas.Count > 0 && motivo.Length < 5)
        {
            TempData["Error"] = "✋ Cambiaste notas que ya estaban puestas: escribe el motivo del cambio (queda registrado y se avisa al profesor/a o a la UTP). No se guardó nada.";
            return RedirectToAction(nameof(Libro), new { cursoId, ramo, editar = true });
        }

        // Columnas existentes: título, ponderación y "eliminar"
        foreach (var ev in evals)
        {
            if (form[$"del_{ev.Id}"] == "1")
            {
                _context.Calificaciones.RemoveRange(existentes.Where(c => c.EvaluacionId == ev.Id));
                _context.Evaluaciones.Remove(ev);
                cambios++;
                continue;
            }
            var t = form[$"t_{ev.Id}"].ToString().Trim();
            if (t.Length > 0 && t != ev.Titulo) { ev.Titulo = t.Length > 60 ? t[..60] : t; cambios++; }
            if (decimal.TryParse(form[$"p_{ev.Id}"].ToString().Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var pond) && pond > 0 && pond != ev.Ponderacion)
            { ev.Ponderacion = Math.Min(pond, 100m); cambios++; }

            foreach (var est in alumnos)
            {
                var txt = form[$"n_{est}_{ev.Id}"].ToString();
                var actual = existentes.FirstOrDefault(c => c.EvaluacionId == ev.Id && c.EstudianteId == est);
                if (string.IsNullOrWhiteSpace(txt))
                {
                    if (actual != null) { _context.Calificaciones.Remove(actual); cambios++; }
                    continue;
                }
                var n = Csv.Nota(txt);
                if (n == null) { invalidas++; continue; }
                if (actual == null)
                {
                    _context.Calificaciones.Add(new Calificacion { EstudianteId = est, EvaluacionId = ev.Id, AsignaturaId = asig.Id, Nota = n.Value, Ponderacion = ev.Ponderacion, Descripcion = ev.Titulo, Fecha = DateTime.UtcNow });
                    cambios++;
                }
                else if (actual.Nota != n.Value) { actual.Nota = n.Value; actual.Fecha = DateTime.UtcNow; cambios++; }
            }
        }

        // Columna nueva: se crea solo si se escribió al menos una nota en ella
        var nuevas = alumnos.Select(est => (est, txt: form[$"n_{est}_nueva"].ToString())).Where(x => !string.IsNullOrWhiteSpace(x.txt)).ToList();
        if (nuevas.Count > 0)
        {
            var titulo = form["t_nueva"].ToString().Trim();
            if (titulo.Length == 0) titulo = "N" + (evals.Count(e => _context.Entry(e).State != EntityState.Deleted) + 1);
            var fecha = Csv.Fecha(form["f_nueva"]) ?? DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Utc);
            var ev = new Evaluacion { Titulo = titulo, AsignaturaId = asig.Id, CursoId = cursoId, Fecha = fecha, Ponderacion = 1m };
            _context.Evaluaciones.Add(ev);
            await _context.SaveChangesAsync();
            foreach (var (est, txt) in nuevas)
            {
                var n = Csv.Nota(txt);
                if (n == null) { invalidas++; continue; }
                _context.Calificaciones.Add(new Calificacion { EstudianteId = est, EvaluacionId = ev.Id, AsignaturaId = asig.Id, Nota = n.Value, Ponderacion = 1m, Descripcion = titulo, Fecha = DateTime.UtcNow });
                cambios++;
            }
        }

        await _context.SaveChangesAsync();
        var avisados = modificadas.Count > 0 ? await AvisarCambioAsync(curso, ramo, asig, modificadas, motivo) : 0;
        // Clubes: si alguien baja de 4,0 (o sube después de salir por notas), se avisa a quien corresponde
        try { await Clubes.RevisarRendimientoAsync(_context, ColegioId(), alumnos); } catch { /* el aviso nunca impide guardar notas */ }
        TempData["SuccessMessage"] = cambios == 0 ? "No había cambios." : $"✅ Libro de {ramo} guardado."
            + (avisados > 0 ? $" Se avisó del cambio a {(EsUtp ? "el profesor/a de la asignatura" : "la UTP")}." : "");
        if (invalidas > 0) TempData["Error"] = $"{invalidas} {(invalidas == 1 ? "nota no se guardó" : "notas no se guardaron")}: deben ir de 1,0 a 7,0.";
        return RedirectToAction(nameof(Libro), new { cursoId, ramo });
    }

    /// <summary>
    /// Aviso en la campana cuando se cambian notas ya puestas: si las cambia la UTP, le llega al profesor/a del ramo;
    /// si las cambia el profesor/a, le llega a la UTP. Nunca al alumno/a ni al apoderado/a.
    /// </summary>
    private async Task<int> AvisarCambioAsync(Curso curso, string ramo, Asignatura asig, List<string> detalle, string motivo)
    {
        var yo = UsuarioId();
        var colegioId = ColegioId();
        List<int> destino;
        if (EsUtp)
        {
            var k = Csv.Clave(ramo);
            destino = (await _context.ClasesHorario.Where(c => c.CursoId == curso.Id && c.ProfesorId != null)
                    .Select(c => new { c.Asignatura, c.ProfesorId }).ToListAsync())
                .Where(x => Csv.Clave(x.Asignatura) == k).Select(x => x.ProfesorId!.Value).ToList();
            destino.Add(asig.DocenteId);
        }
        else
        {
            destino = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync();
        }
        destino = destino.Where(id => id != yo).Distinct().ToList();
        if (destino.Count == 0) return 0;

        var quien = (User.Identity?.Name ?? "") + (EsUtp ? " (UTP)" : " (profesor/a)");
        var cursoTxt = (curso.Nombre + " " + curso.Letra).Trim();
        var msg = $"{quien} cambió {detalle.Count} {(detalle.Count == 1 ? "nota" : "notas")} de {ramo} en {cursoTxt}: "
            + string.Join("; ", detalle.Take(6)) + (detalle.Count > 6 ? "; …" : "") + $". Motivo: {motivo}";
        if (msg.Length > 1500) msg = msg[..1500];
        var enlace = $"/Notas/Libro?cursoId={curso.Id}&ramo={Uri.EscapeDataString(ramo)}";
        foreach (var id in destino)
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = colegioId, UsuarioId = id, Titulo = $"✏️ Cambio de notas · {ramo} {cursoTxt}",
                Mensaje = msg, Enlace = enlace, CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
        await _context.SaveChangesAsync();
        return destino.Count;
    }

    // ================================================================ Exportar
    /// <summary>Descarga en Excel (CSV): un ramo con todas sus notas, o el resumen del curso con el promedio por ramo.</summary>
    [HttpGet]
    public async Task<IActionResult> Exportar(int cursoId, string? ramo)
    {
        var r = await Libro(cursoId, ramo) as ViewResult;
        if (r?.Model is not LibroVm vm) return RedirectToAction(nameof(Index));
        var filas = new List<List<string?>>();
        var nombreCurso = $"{vm.Curso.Nombre} {vm.Curso.Letra}".Trim();
        if (vm.Ramo != null)
        {
            filas.Add(new List<string?> { "N°", "RUT", "Alumno/a" }.Concat(vm.Columnas.Select(c => c.Titulo)).Append("Promedio").ToList());
            var i = 0;
            foreach (var f in vm.Filas)
                filas.Add(new List<string?> { (++i).ToString(), f.Rut, f.Nombre }
                    .Concat(vm.Columnas.Select(c => f.Notas.TryGetValue(c.Id, out var n) ? Csv.Nota1(n) : ""))
                    .Append(f.Promedio is decimal p ? Csv.Nota1(p) : "").ToList());
        }
        else
        {
            filas.Add(new List<string?> { "N°", "RUT", "Alumno/a" }.Concat(vm.Ramos.Select(x => x.Nombre)).Append("Promedio general").ToList());
            var i = 0;
            foreach (var f in vm.Filas)
                filas.Add(new List<string?> { (++i).ToString(), f.Rut, f.Nombre }
                    .Concat(vm.Ramos.Select(x => vm.Resumen[f.EstudianteId].TryGetValue(x.Nombre, out var n) ? Csv.Nota1(n) : ""))
                    .Append(f.Promedio is decimal p ? Csv.Nota1(p) : "").ToList());
        }
        var archivo = $"notas_{nombreCurso}{(vm.Ramo != null ? "_" + vm.Ramo : "")}.csv".Replace(' ', '_').Replace(",", "");
        return File(Csv.Escribir(filas), "text/csv", archivo);
    }

    // ================================================================ Importar
    /// <summary>
    /// Importa notas desde Excel (CSV). Columnas: RUT alumno; Asignatura; Evaluación; Nota; Fecha (opcional); Ponderación (opcional).
    /// El curso sale del alumno/a. Se cruza por RUT: si la nota de esa evaluación ya existe, se actualiza (no se duplica).
    /// </summary>
    [Authorize(Roles = "UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Importar(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var pl = await Csv.LeerAsync(archivo);
        int iRut = pl.Col("rut"), iAsig = pl.Col("asignatura", "ramo"), iEv = pl.Col("evaluacion", "nota n", "titulo"),
            iNota = pl.Col("nota", "calificacion"), iFec = pl.Col("fecha"), iPond = pl.Col("ponderacion");
        if (iEv == iNota) iEv = pl.Col("evaluacion", "titulo");
        if (iRut < 0 || iAsig < 0 || iNota < 0) { TempData["Error"] = "Faltan columnas. Usa la plantilla: RUT alumno, Asignatura, Evaluación y Nota."; return RedirectToAction(nameof(Index)); }
        if (pl.Filas.Count > 30000) { TempData["Error"] = "Máximo 30.000 notas por archivo."; return RedirectToAction(nameof(Index)); }

        var anio = DateTime.Now.Year;
        var colegioId = ColegioId();
        // Alumnos/as del colegio por RUT (activos y en un curso de este año)
        var estudiantes = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null && e.Curso.AnioAcademico == anio).ToListAsync();
        var porRut = estudiantes.GroupBy(e => Rut.Normalizar(e.Usuario!.Rut)).ToDictionary(g => g.Key, g => g.First());

        var cacheAsig = new Dictionary<string, Asignatura>();
        var cacheEval = new Dictionary<string, Evaluacion>();
        var errores = new List<string>();
        int nuevas = 0, actualizadas = 0, iguales = 0;
        var cambiosPorRamo = new Dictionary<int, (Curso Curso, Asignatura Asig, List<string> Detalle)>();

        // Notas que ya existen (para no duplicar)
        var cursoIds = estudiantes.Select(e => e.CursoId).Distinct().ToList();
        var asigsCol = await _context.Asignaturas.Where(a => cursoIds.Contains(a.CursoId)).ToListAsync();
        foreach (var a in asigsCol) cacheAsig.TryAdd($"{a.CursoId}|{Csv.Clave(a.Nombre)}", a);
        var asigIds = asigsCol.Select(a => a.Id).ToList();
        foreach (var e in await _context.Evaluaciones.Where(e => asigIds.Contains(e.AsignaturaId)).ToListAsync())
            cacheEval.TryAdd($"{e.AsignaturaId}|{Csv.Clave(e.Titulo)}", e);
        var existentes = (await _context.Calificaciones.Where(c => asigIds.Contains(c.AsignaturaId)).ToListAsync())
            .GroupBy(c => (c.EvaluacionId, c.EstudianteId)).ToDictionary(g => g.Key, g => g.First());

        for (int i = 0; i < pl.Filas.Count; i++)
        {
            var f = pl.Filas[i];
            var fila = i + 2;
            string C(int k) => Csv.Planilla.Celda(f, k);
            if (C(iRut).Length == 0 && C(iNota).Length == 0) continue;
            if (!Rut.TryValidar(C(iRut), out var rut, out _)) { errores.Add($"Fila {fila}: RUT no válido ({C(iRut)})."); continue; }
            if (!porRut.TryGetValue(rut, out var est)) { errores.Add($"Fila {fila}: no hay alumno/a con RUT {C(iRut)} este año."); continue; }
            var ramo = C(iAsig);
            if (ramo.Length == 0) { errores.Add($"Fila {fila}: falta la asignatura."); continue; }
            var nota = Csv.Nota(C(iNota));
            if (nota == null) { errores.Add($"Fila {fila}: nota no válida ({C(iNota)}); va de 1,0 a 7,0."); continue; }

            var asig = await AsignaturaAsync(est.Curso!, ramo, cacheAsig);
            var titulo = iEv >= 0 && C(iEv).Length > 0 ? C(iEv) : "N1";
            if (titulo.Length > 60) titulo = titulo[..60];
            var kEv = $"{asig.Id}|{Csv.Clave(titulo)}";
            if (!cacheEval.TryGetValue(kEv, out var ev))
            {
                decimal pond = 1m;
                if (iPond >= 0) decimal.TryParse(C(iPond).Replace(',', '.').Replace("%", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out pond);
                ev = new Evaluacion
                {
                    Titulo = titulo, AsignaturaId = asig.Id, CursoId = est.CursoId, Ponderacion = pond > 0 ? pond : 1m,
                    Fecha = (iFec >= 0 ? Csv.Fecha(C(iFec)) : null) ?? DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Utc)
                };
                _context.Evaluaciones.Add(ev);
                await _context.SaveChangesAsync();
                cacheEval[kEv] = ev;
            }

            if (existentes.TryGetValue((ev.Id, est.Id), out var cal))
            {
                if (cal.Nota == nota.Value) iguales++;
                else
                {
                    if (!cambiosPorRamo.TryGetValue(asig.Id, out var cr)) cambiosPorRamo[asig.Id] = cr = (est.Curso!, asig, new List<string>());
                    cr.Detalle.Add($"{est.Usuario!.Nombre} {est.Usuario.Apellido} {ev.Titulo}: {Csv.Nota1(cal.Nota)} → {Csv.Nota1(nota.Value)}");
                    cal.Nota = nota.Value; cal.Fecha = DateTime.UtcNow; actualizadas++;
                }
            }
            else
            {
                cal = new Calificacion { EstudianteId = est.Id, EvaluacionId = ev.Id, AsignaturaId = asig.Id, Nota = nota.Value, Ponderacion = ev.Ponderacion, Descripcion = ev.Titulo, Fecha = DateTime.UtcNow };
                _context.Calificaciones.Add(cal);
                existentes[(ev.Id, est.Id)] = cal;
                nuevas++;
            }
            if ((nuevas + actualizadas) % 1000 == 0) await _context.SaveChangesAsync();
        }
        await _context.SaveChangesAsync();

        // Notas que ya existían y cambiaron con la planilla: aviso al profesor/a de cada ramo
        try { await Clubes.RevisarRendimientoAsync(_context, colegioId, existentes.Values.Select(c => c.EstudianteId).Distinct().ToList()); } catch { }
        foreach (var (cursoCambio, asigCambio, detalle) in cambiosPorRamo.Values)
            await AvisarCambioAsync(cursoCambio, asigCambio.Nombre, asigCambio, detalle, "corrección por importación de planilla (UTP)");

        TempData["SuccessMessage"] = $"📥 Notas importadas: {nuevas} nuevas, {actualizadas} actualizadas" + (iguales > 0 ? $", {iguales} ya estaban igual" : "") + "."
            + (cambiosPorRamo.Count > 0 ? " Se avisó a los profesores/as de las notas que cambiaron." : "");
        if (errores.Count > 0) TempData["Error"] = $"{errores.Count} {(errores.Count == 1 ? "fila no se importó" : "filas no se importaron")}: " + string.Join(" ", errores.Take(8)) + (errores.Count > 8 ? " …" : "");
        return RedirectToAction(nameof(Index));
    }
}
