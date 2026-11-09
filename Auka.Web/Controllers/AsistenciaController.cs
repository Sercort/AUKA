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
/// Asistencia por bloque: el profesor/a pasa la lista en su clase (presente, ausente o atrasado con su hora y justificativo)
/// y al terminar "finaliza la clase": desde ahí solo la UTP puede corregirla.
/// Los atrasos de la entrada los registra inspectoría (en la mañana) y llegan solos a la lista de cada clase.
/// Ven los porcentajes: la UTP y la dirección siempre; los colegas, si el profesor/a jefe del curso lo permite.
/// Inspectoría no ve ni corrige la asistencia (solo registra los atrasos de llegada).
/// Alertas: asistencia anual bajo 85% (profesor/a jefe y apoderado/a) y 3 días con atraso en el mes (profesor/a jefe y UTP).
/// </summary>
[Authorize(Roles = "Docente,UTP,Director,SuperAdmin,Orientadora")]
public class AsistenciaController : Controller
{
    private readonly ApplicationDbContext _context;
    public AsistenciaController(ApplicationDbContext context) => _context = context;

    public const decimal Minimo = 85m;
    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }
    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }
    // Corregir listas ya finalizadas: solo la UTP
    private bool Corrige => User.IsInRole("UTP");
    // Ven todos los porcentajes siempre: UTP y dirección
    private bool VeTodo => User.IsInRole("UTP") || User.IsInRole("Director") || User.IsInRole("SuperAdmin");

    /// <summary>Fecha guardada: el día local a mediodía UTC (así se agrupa bien por día).</summary>
    public static DateTime DiaUtc(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
    /// <summary>
    /// Importa asistencia histórica (ej. de marzo a noviembre) desde una planilla CSV. Solo UTP y administración.
    /// Columnas: Fecha; Nivel; Letra; RUT; Estado; Hora llegada; Motivo.
    /// Cada fecha + curso que aparece queda como "lista pasada" para TODO el curso: los que no vienen en la planilla
    /// quedan presentes. Una fila con el RUT vacío solo marca el día como pasado (todos presentes).
    /// Se guarda en la primera clase de ese día. No pisa listas ya pasadas: ese curso y día se salta.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "UTP,SuperAdmin,Administrador")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Importar(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var colegioId = ColegioId();
        var yo = UsuarioId();
        var pl = await Csv.LeerAsync(archivo);
        int iFec = pl.Col("fecha"), iNiv = pl.Col("nivel", "curso"), iLet = pl.Col("letra"), iRut = pl.Col("rut"), iEst = pl.Col("estado"),
            iHora = pl.Col("hora"), iMot = pl.Col("motivo", "observacion", "justificativo");
        if (iFec < 0 || iNiv < 0 || iEst < 0)
        {
            TempData["Error"] = "Faltan columnas. Usa la plantilla: Fecha, Nivel, Letra, RUT, Estado, Hora llegada y Motivo.";
            return RedirectToAction(nameof(Index));
        }
        string C(string[] f, int i) => Csv.Planilla.Celda(f, i);
        var anio = DateTime.Now.Year;
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync();
        var cursoIds = cursos.Select(c => c.Id).ToList();
        var alumnos = await _context.Estudiantes.AsNoTracking().Where(e => cursoIds.Contains(e.CursoId) && e.Usuario != null)
            .Select(e => new { e.Id, e.CursoId, e.Usuario!.Rut }).ToListAsync();
        var porRut = alumnos.GroupBy(a => Auka.Web.Services.Rut.Normalizar(a.Rut)).ToDictionary(g => g.Key, g => g.First());
        var porCurso = alumnos.GroupBy(a => a.CursoId).ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());
        // Primera clase de cada curso cada día (ahí queda la lista del día)
        var primeras = (await _context.ClasesHorario.AsNoTracking().Include(c => c.Bloque).Where(c => cursoIds.Contains(c.CursoId) && c.Bloque != null).ToListAsync())
            .GroupBy(c => (c.CursoId, c.Dia)).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Bloque!.HoraInicio).First());
        var asignaturas = await _context.Asignaturas.Where(a => cursoIds.Contains(a.CursoId)).ToListAsync();

        // 1. Leer filas y agrupar por curso + día
        var errores = new List<string>();
        var grupos = new Dictionary<(int Curso, DateOnly Dia), Dictionary<int, (EstadoAsistencia Est, TimeSpan? Hora, string? Motivo)>>();
        var hoy = Hoy;
        var fila = 1;
        foreach (var f in pl.Filas)
        {
            fila++;
            var fecha = Csv.Fecha(C(f, iFec));
            if (fecha == null) { errores.Add($"Fila {fila}: fecha no válida ({C(f, iFec)})."); continue; }
            var dia = DateOnly.FromDateTime(fecha.Value);
            if (dia > hoy || dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { errores.Add($"Fila {fila}: {dia:dd-MM-yyyy} es fin de semana o futuro."); continue; }
            var nivel = C(f, iNiv); var letra = C(f, iLet).ToUpperInvariant();
            var curso = cursos.FirstOrDefault(c => Niveles.Normal(c.Nombre) == Niveles.Normal(nivel) && (letra.Length == 0 || (c.Letra ?? "").Trim().ToUpperInvariant() == letra));
            if (curso == null) { errores.Add($"Fila {fila}: no existe el curso {nivel} {letra} este año."); continue; }
            if (!grupos.TryGetValue((curso.Id, dia), out var g)) grupos[(curso.Id, dia)] = g = new();
            var rutTxt = C(f, iRut);
            if (rutTxt.Length == 0) continue;   // solo marca el día como pasado
            if (!porRut.TryGetValue(Auka.Web.Services.Rut.Normalizar(rutTxt), out var al) || al.CursoId != curso.Id)
            { errores.Add($"Fila {fila}: el RUT {rutTxt} no está en {curso.Nombre} {curso.Letra}."); continue; }
            var e = Csv.Clave(C(f, iEst));
            EstadoAsistencia est = e.StartsWith("a") && !e.StartsWith("at") ? EstadoAsistencia.Ausente
                : e.StartsWith("at") || e == "t" ? EstadoAsistencia.Atrasado
                : e.StartsWith("j") ? EstadoAsistencia.Justificado : EstadoAsistencia.Presente;
            TimeSpan? hora = est == EstadoAsistencia.Atrasado && TimeSpan.TryParse(C(f, iHora), out var h) ? h : null;
            var mot = C(f, iMot);
            if (est == EstadoAsistencia.Atrasado && hora == null) hora = new TimeSpan(8, 15, 0);
            g[al.Id] = (est, hora, mot.Length > 0 ? mot[..Math.Min(mot.Length, 300)] : (est == EstadoAsistencia.Atrasado ? "Sin justificativo" : null));
        }
        if (grupos.Count == 0)
        {
            TempData["Error"] = "No se importó nada. " + string.Join(" ", errores.Take(5));
            return RedirectToAction(nameof(Index));
        }

        // 2. Días que ya tienen lista: no se tocan
        var desde = DiaUtc(grupos.Keys.Min(k => k.Dia));
        var hasta = DiaUtc(grupos.Keys.Max(k => k.Dia));
        var yaPasadas = (await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.Fecha >= desde && a.Fecha <= hasta)
                .Select(a => new { C = a.CursoId!.Value, a.Fecha }).Distinct().ToListAsync())
            .Select(x => (x.C, DateOnly.FromDateTime(x.Fecha))).ToHashSet();

        // 3. Crear los registros (todo el curso; los que no vienen quedan presentes)
        async Task<int> AsignaturaAsync(Curso curso, string ramo, int? profe)
        {
            var a = asignaturas.FirstOrDefault(x => x.CursoId == curso.Id && Csv.Clave(x.Nombre) == Csv.Clave(ramo));
            if (a == null)
            {
                a = new Asignatura { Nombre = ramo, Codigo = "", CursoId = curso.Id, DocenteId = profe ?? curso.ProfesorJefeId ?? yo };
                _context.Asignaturas.Add(a);
                await _context.SaveChangesAsync();
                asignaturas.Add(a);
            }
            return a.Id;
        }
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        int dias = 0, saltados = 0, registros = 0, ausentes = 0;
        var porGuardar = 0;
        foreach (var ((cursoId, dia), excepciones) in grupos.OrderBy(k => k.Key.Dia))
        {
            if (yaPasadas.Contains((cursoId, dia))) { saltados++; continue; }
            var curso = cursos.First(c => c.Id == cursoId);
            primeras.TryGetValue((cursoId, (int)dia.DayOfWeek), out var clase);
            var asigId = await AsignaturaAsync(curso, clase?.Asignatura ?? asignaturas.FirstOrDefault(a => a.CursoId == cursoId)?.Nombre ?? "Clase", clase?.ProfesorId);
            var fechaUtc = DiaUtc(dia);
            var cierre = dia.ToDateTime(TimeOnly.MinValue).Add(clase?.Bloque?.HoraFin ?? new TimeSpan(9, 30, 0)).ToUniversalTime();
            foreach (var id in porCurso.GetValueOrDefault(cursoId) ?? new List<int>())
            {
                var (est, hora, mot) = excepciones.TryGetValue(id, out var x) ? x : (EstadoAsistencia.Presente, (TimeSpan?)null, (string?)null);
                _context.Asistencias.Add(new Asistencia
                {
                    EstudianteId = id, AsignaturaId = asigId, Fecha = fechaUtc, CursoId = cursoId, BloqueJornadaId = clase?.BloqueJornadaId,
                    Estado = est, Presente = est != EstadoAsistencia.Ausente, HoraLlegada = hora, Observacion = mot,
                    RegistradoPorId = clase?.ProfesorId ?? yo, CerradaUtc = cierre
                });
                registros++; porGuardar++;
                if (est == EstadoAsistencia.Ausente) ausentes++;
            }
            dias++;
            if (porGuardar >= 4000) { _context.ChangeTracker.DetectChanges(); await _context.SaveChangesAsync(); _context.ChangeTracker.Clear(); porGuardar = 0; }
        }
        _context.ChangeTracker.DetectChanges();
        await _context.SaveChangesAsync();
        _context.ChangeTracker.AutoDetectChangesEnabled = true;

        var pct = registros > 0 ? Math.Round(100m * (registros - ausentes) / registros, 1) : 0;
        TempData["SuccessMessage"] = $"✅ Asistencia importada: {dias} listas (curso y día) con {registros:N0} registros · {pct}% de asistencia."
            + (saltados > 0 ? $" Se saltaron {saltados} listas que ya estaban pasadas." : "")
            + (errores.Count > 0 ? $" {errores.Count} filas con problemas: " + string.Join(" ", errores.Take(4)) : "");
        return RedirectToAction(nameof(Index));
    }

    public static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    /// <summary>Horas después del término de la clase en que la lista se cierra sola.</summary>
    public const int HorasCierre = 2;
    /// <summary>¿Ya pasó el plazo para pasar la lista? (2 horas después de terminar la clase, o el día ya pasó)</summary>
    public static bool Vencida(DateOnly dia, TimeSpan fin) => DateTime.Now > dia.ToDateTime(TimeOnly.MinValue).Add(fin).AddHours(HorasCierre);

    /// <summary>% de asistencia (presentes + atrasados + justificados sobre el total de registros).</summary>
    public static decimal? Porcentaje(int presentes, int total) => total == 0 ? null : Math.Round(presentes * 100m / total, 1, MidpointRounding.AwayFromZero);

    public sealed record ClaseHoy(int CursoId, string Curso, int BloqueId, TimeSpan Inicio, TimeSpan Fin, string Asignatura, int Registros, int Alumnos, bool EsMia,
        string? Sesion = null, string? Reemplazo = null);

    // ================================================================ Entrada
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? fecha)
    {
        var colegioId = ColegioId();
        var yo = UsuarioId();
        var dia = fecha ?? Hoy;
        var dow = (int)dia.DayOfWeek;   // 1 = lunes ... 5 = viernes
        ViewBag.Fecha = dia;

        // Clases de ese día: las mías (docente) o todas (UTP/inspector)
        var q = _context.ClasesHorario.AsNoTracking().Include(c => c.Curso).Include(c => c.Bloque)
            .Where(c => c.Dia == dow && c.Curso != null && c.Curso.AnioAcademico == dia.Year && c.Bloque != null);
        var reemplazos = await ClasesEnCurso.ReemplazosAsync(_context, colegioId, dia);
        var misReemplazos = reemplazos.Where(r => r.Value.ProfesorId == yo).Select(r => r.Key).ToList();
        var cursosReemplazo = misReemplazos.Select(r => r.Curso).ToList();
        if (!VeTodo) q = q.Where(c => c.ProfesorId == yo || cursosReemplazo.Contains(c.CursoId));
        var clases = (await q.ToListAsync()).Where(c => VeTodo || c.ProfesorId == yo || misReemplazos.Contains((c.CursoId, c.BloqueJornadaId))).ToList();
        var sesiones = await ClasesEnCurso.DelDiaAsync(_context, colegioId, dia);
        if (Corrige)
            ViewBag.Docentes = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
                .OrderBy(u => u.Apellido).Select(u => new OpcionVm(u.Id, u.Nombre + " " + u.Apellido)).ToListAsync();
        var fechaUtc = DiaUtc(dia);
        var cursoIds = clases.Select(c => c.CursoId).Distinct().ToList();
        var registros = await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.Fecha == fechaUtc)
            .GroupBy(a => new { a.CursoId, a.BloqueJornadaId }).Select(g => new { g.Key.CursoId, g.Key.BloqueJornadaId, N = g.Count() }).ToListAsync();
        var alumnos = await _context.Estudiantes.Where(e => cursoIds.Contains(e.CursoId)).GroupBy(e => e.CursoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        ViewBag.Clases = clases
            .OrderBy(c => c.Bloque!.HoraInicio).ThenBy(c => Niveles.Orden(c.Curso!.Nombre)).ThenBy(c => c.Curso!.Letra)
            .Select(c => new ClaseHoy(c.CursoId, $"{c.Curso!.Nombre} {c.Curso.Letra}".Trim(), c.BloqueJornadaId, c.Bloque!.HoraInicio, c.Bloque.HoraFin, c.Asignatura,
                registros.FirstOrDefault(r => r.CursoId == c.CursoId && r.BloqueJornadaId == c.BloqueJornadaId)?.N ?? 0,
                alumnos.GetValueOrDefault(c.CursoId),
                reemplazos.TryGetValue((c.CursoId, c.BloqueJornadaId), out var rr) ? rr.ProfesorId == yo : c.ProfesorId == yo,
                sesiones.TryGetValue((c.CursoId, c.BloqueJornadaId), out var se) ? ClasesEnCurso.Texto(se) : null,
                reemplazos.TryGetValue((c.CursoId, c.BloqueJornadaId), out var r2) ? r2.Nombre : null)).ToList();

        // Carpetas de cursos con su % de asistencia del año (para mirar el detalle)
        var anio = dia.Year;
        // Todos los cursos se pueden mirar (sin modificar). El % se oculta a los colegas si el profesor/a jefe no lo comparte.
        var cursos = await _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe).Where(c => c.AnioAcademico == anio && c.Nombre != "Pacientes").ToListAsync();
        var ids = cursos.Select(c => c.Id).ToList();
        var ini = DiaUtc(new DateOnly(anio, 1, 1));
        var stats = await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && ids.Contains(a.CursoId.Value) && a.Fecha >= ini)
            .GroupBy(a => a.CursoId!.Value).Select(g => new { g.Key, P = g.Count(x => x.Estado != EstadoAsistencia.Ausente), T = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => (x.P, x.T));
        var nAl = await _context.Estudiantes.Where(e => ids.Contains(e.CursoId)).GroupBy(e => e.CursoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        ViewBag.Carpetas = new CarpetasVm
        {
            Clave = "asistencia",
            Cursos = cursos.Select(c =>
            {
                var visible = PuedeVerPorcentaje(c, yo);
                var p = visible && stats.TryGetValue(c.Id, out var s) ? Porcentaje(s.P, s.T) : null;
                return new CarpetaCurso(c.Id, c.Nombre, c.Letra, visible ? Url.Action("Curso", new { id = c.Id })! : "#", nAl.GetValueOrDefault(c.Id), c.CapacidadMaxima,
                    c.ProfesorJefe != null ? $"{c.ProfesorJefe.Nombre} {c.ProfesorJefe.Apellido}" : "sin prof. jefe",
                    !visible ? "🔒 El profesor/a jefe no comparte el %" : p != null ? $"✅ {p:0.#}% asistencia" : "✅ Sin registros", null, null, null,
                    p != null && p < Minimo ? "alerta" : null, p != null && p < Minimo ? "Bajo 85%" : null);
            }).ToList()
        };
        return View();
    }

    /// <summary>¿Ve el % de este curso? UTP y dirección siempre; el profesor/a jefe; los colegas si el jefe lo comparte.</summary>
    private bool PuedeVerPorcentaje(Curso c, int yo) => VeTodo || c.ProfesorJefeId == yo || c.AsistenciaVisibleColegas;

    /// <summary>El profesor/a jefe (o la UTP) decide si sus colegas ven el % de asistencia de su curso.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Compartir(int cursoId, bool visible)
    {
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        if (curso.ProfesorJefeId != UsuarioId() && !Corrige) return RedirectToAction("AccesoDenegado", "Account");
        curso.AsistenciaVisibleColegas = visible;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = visible ? "👀 Tus colegas pueden ver el % de asistencia del curso." : "🔒 Listo: el % del curso queda solo para ti, la UTP y la dirección.";
        return RedirectToAction(nameof(Curso), new { id = cursoId });
    }

    // ================================================================ Pasar lista
    public sealed record FilaLista(int EstudianteId, string Nombre, string Rut, EstadoAsistencia? Estado, TimeSpan? Llegada, decimal? PorcentajeAnual, int AtrasosMes,
        string? Observacion, TimeSpan? LlegadaInspectoria, string? MotivoInspectoria);

    /// <summary>
    /// ¿Esta clase de ese día es suya? La del horario; pero si la UTP le asignó un reemplazo ese día, es solo del reemplazo.
    /// </summary>
    private async Task<bool> EsSuClaseAsync(int cursoId, int bloqueId, DateOnly dia)
    {
        var f = DiaUtc(dia);
        var reemplazo = await _context.ReemplazosClase.AsNoTracking().Where(r => r.CursoId == cursoId && r.BloqueJornadaId == bloqueId && r.Fecha == f)
            .OrderByDescending(r => r.CreadoUtc).Select(r => (int?)r.ProfesorId).FirstOrDefaultAsync();
        if (reemplazo != null) return reemplazo == UsuarioId();
        var dow = (int)dia.DayOfWeek;
        return await _context.ClasesHorario.AnyAsync(c => c.CursoId == cursoId && c.BloqueJornadaId == bloqueId && c.Dia == dow && c.ProfesorId == UsuarioId());
    }

    /// <summary>
    /// ▶️ Iniciar clase: la inicia solo quien la hace (o su reemplazo). Queda registrada como "clase iniciada"
    /// (la ven la UTP, inspectoría, alumnos/as y apoderados/as en el horario) y se abre la lista del curso.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarClase(int cursoId, int bloqueId)
    {
        var dia = Hoy;
        if (!await EsSuClaseAsync(cursoId, bloqueId, dia))
        {
            TempData["Error"] = "Solo el profesor/a de esta clase (o el reemplazo que asignó la UTP) puede iniciarla.";
            return RedirectToAction(nameof(Index));
        }
        var f = DiaUtc(dia);
        if (!await _context.SesionesClase.AnyAsync(x => x.CursoId == cursoId && x.BloqueJornadaId == bloqueId && x.Fecha == f))
        {
            var esReemplazo = await _context.ReemplazosClase.AnyAsync(r => r.CursoId == cursoId && r.BloqueJornadaId == bloqueId && r.Fecha == f && r.ProfesorId == UsuarioId());
            _context.SesionesClase.Add(new SesionClase { ColegioId = ColegioId(), CursoId = cursoId, BloqueJornadaId = bloqueId, Fecha = f, ProfesorId = UsuarioId(), InicioUtc = DateTime.UtcNow, Reemplazo = esReemplazo });
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Lista), new { cursoId, bloqueId });
    }

    /// <summary>La UTP asigna (o quita, con profesorId 0) un profesor/a de reemplazo para una clase de un día.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AsignarReemplazo(int cursoId, int bloqueId, DateOnly fecha, int profesorId, string? motivo)
    {
        if (!Corrige) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var f = DiaUtc(fecha);
        _context.ReemplazosClase.RemoveRange(await _context.ReemplazosClase.Where(r => r.CursoId == cursoId && r.BloqueJornadaId == bloqueId && r.Fecha == f).ToListAsync());
        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId);
        var bloque = await _context.BloquesJornada.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bloqueId);
        var clase = await _context.ClasesHorario.AsNoTracking().FirstOrDefaultAsync(c => c.CursoId == cursoId && c.BloqueJornadaId == bloqueId && c.Dia == (int)fecha.DayOfWeek);
        if (profesorId > 0)
        {
            var prof = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == profesorId && u.ColegioId == colegioId && u.Activo);
            if (prof == null || curso == null || bloque == null) { TempData["Error"] = "Profesor/a o clase no válidos."; return RedirectToAction(nameof(Index), new { fecha = fecha.ToString("yyyy-MM-dd") }); }
            _context.ReemplazosClase.Add(new ReemplazoClase { ColegioId = colegioId, CursoId = cursoId, BloqueJornadaId = bloqueId, Fecha = f, ProfesorId = profesorId,
                TitularId = clase?.ProfesorId, Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()[..Math.Min(motivo.Trim().Length, 200)], AsignadoPorId = UsuarioId() });
            var cuando = $"{fecha:dd-MM} {bloque.HoraInicio.ToString("hh\\:mm")}";
            _context.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = profesorId, Titulo = $"🔁 Reemplazo: {curso.Nombre} {curso.Letra} · {cuando}",
                Mensaje = $"La UTP te asignó para reemplazar {clase?.Asignatura ?? "la clase"} en {curso.Nombre} {curso.Letra} ({cuando}). Tú inicias la clase y pasas la lista.",
                Enlace = $"/Asistencia?fecha={fecha:yyyy-MM-dd}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
            TempData["SuccessMessage"] = $"🔁 Reemplazo asignado: {prof.Nombre} {prof.Apellido} hará {clase?.Asignatura} en {curso.Nombre} {curso.Letra} ({cuando}).";
        }
        else TempData["SuccessMessage"] = "Se quitó el reemplazo: la clase vuelve a su profesor/a.";
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { fecha = fecha.ToString("yyyy-MM-dd") });
    }

    /// <summary>¿Puede pasar o corregir esta lista? El profesor/a de la clase mientras no la finalice; la UTP siempre.</summary>
    private async Task<bool> PuedeEditarAsync(int cursoId, int bloqueId, DateOnly dia)
    {
        if (Corrige) return true;
        if (!await EsSuClaseAsync(cursoId, bloqueId, dia)) return false;
        // Se cierra sola 2 horas después de terminar la clase: después solo la UTP
        var fin = await _context.BloquesJornada.AsNoTracking().Where(b => b.Id == bloqueId).Select(b => (TimeSpan?)b.HoraFin).FirstOrDefaultAsync();
        if (fin != null && Vencida(dia, fin.Value)) return false;
        var f = DiaUtc(dia);
        return !await _context.Asistencias.AnyAsync(a => a.CursoId == cursoId && a.BloqueJornadaId == bloqueId && a.Fecha == f && a.CerradaUtc != null);
    }

    [HttpGet]
    public async Task<IActionResult> Lista(int cursoId, int bloqueId, DateOnly? fecha)
    {
        var dia = fecha ?? Hoy;
        var dow = (int)dia.DayOfWeek;
        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId);
        var bloque = await _context.BloquesJornada.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bloqueId);
        if (curso == null || bloque == null) return NotFound();
        var clase = await _context.ClasesHorario.AsNoTracking().FirstOrDefaultAsync(c => c.CursoId == cursoId && c.BloqueJornadaId == bloqueId && c.Dia == dow);
        var puede = await PuedeEditarAsync(cursoId, bloqueId, dia);
        var esSuya = await EsSuClaseAsync(cursoId, bloqueId, dia);
        if (!puede && !esSuya && !VeTodo && curso.ProfesorJefeId != UsuarioId())
            return RedirectToAction("AccesoDenegado", "Account");

        var alumnos = await _context.Estudiantes.AsNoTracking().Include(e => e.Usuario)
            .Where(e => e.CursoId == cursoId && e.Usuario != null && e.Usuario.Activo).ToListAsync();
        var ids = alumnos.Select(a => a.Id).ToList();
        var fechaUtc = DiaUtc(dia);
        var hoyReg = await _context.Asistencias.AsNoTracking().Where(a => ids.Contains(a.EstudianteId) && a.Fecha == fechaUtc && a.BloqueJornadaId == bloqueId).ToListAsync();
        var anual = await ResumenAsync(ids, dia.Year);
        var inicioMes = DiaUtc(new DateOnly(dia.Year, dia.Month, 1));
        var atrasos = await _context.Asistencias.AsNoTracking().Where(a => ids.Contains(a.EstudianteId) && a.Fecha >= inicioMes && a.Estado == EstadoAsistencia.Atrasado)
            .GroupBy(a => new { a.EstudianteId, a.Fecha }).Select(g => g.Key.EstudianteId).ToListAsync();

        ViewBag.Curso = curso; ViewBag.Bloque = bloque; ViewBag.Fecha = dia; ViewBag.Asignatura = clase?.Asignatura ?? "Clase";
        ViewBag.Puede = puede; ViewBag.YaTomada = hoyReg.Count > 0;
        ViewBag.Cerrada = hoyReg.Any(r => r.CerradaUtc != null);
        ViewBag.Vencida = Vencida(dia, bloque.HoraFin);
        ViewBag.EsSuya = esSuya;
        ViewBag.EsUtp = Corrige;
        // Atrasos de la entrada (inspectoría): si llegó durante la clase → atrasado; si llegó después → ausente por atraso
        var llegadas = (await _context.AtrasosLlegada.AsNoTracking().Where(x => ids.Contains(x.EstudianteId) && x.Fecha == fechaUtc).ToListAsync())
            .GroupBy(x => x.EstudianteId).Select(g => g.OrderBy(x => x.Hora).First()).ToList();
        var filas = alumnos.OrderBy(a => a.Usuario!.Apellido).ThenBy(a => a.Usuario!.Nombre).Select(a =>
        {
            var r = hoyReg.FirstOrDefault(x => x.EstudianteId == a.Id);
            var ll = llegadas.FirstOrDefault(x => x.EstudianteId == a.Id);
            EstadoAsistencia? sugerido = r?.Estado;
            TimeSpan? hora = r?.HoraLlegada;
            var obs = r?.Observacion;
            if (r == null && ll != null && ll.Hora > bloque.HoraInicio)
            {
                if (ll.Hora < bloque.HoraFin) { sugerido = EstadoAsistencia.Atrasado; hora = ll.Hora; obs = $"Atraso registrado por inspectoría: {ll.Motivo}"; }
                else { sugerido = EstadoAsistencia.Ausente; obs = $"Ausente por atraso (llegó {ll.Hora:hh\\:mm}, inspectoría)"; }
            }
            return new FilaLista(a.Id, $"{a.Usuario!.Apellido}, {a.Usuario.Nombre}", a.Usuario.Rut, sugerido, hora,
                anual.TryGetValue(a.Id, out var p) ? p.Porcentaje : null, atrasos.Count(x => x == a.Id), obs, ll?.Hora, ll?.Motivo);
        }).ToList();
        return View(filas);
    }

    /// <summary>Guarda la lista (si ya existía, la actualiza: no duplica). Después revisa las alertas.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(int cursoId, int bloqueId, DateOnly fecha, bool finalizar = false)
    {
        var dow = (int)fecha.DayOfWeek;
        if (!await PuedeEditarAsync(cursoId, bloqueId, fecha))
        {
            TempData["Error"] = "🔒 Esta lista ya está cerrada (se finalizó o pasaron más de 2 horas desde que terminó la clase): solo la UTP puede corregirla.";
            return RedirectToAction(nameof(Lista), new { cursoId, bloqueId, fecha = fecha.ToString("yyyy-MM-dd") });
        }
        if (fecha > Hoy) { TempData["Error"] = "No se puede pasar lista de un día que aún no llega."; return RedirectToAction(nameof(Index)); }
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return NotFound();
        var clase = await _context.ClasesHorario.AsNoTracking().FirstOrDefaultAsync(c => c.CursoId == cursoId && c.BloqueJornadaId == bloqueId && c.Dia == dow);
        var bloque = await _context.BloquesJornada.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bloqueId);
        // La asistencia se asocia a la asignatura de esa clase (se crea si aún no existe)
        var ramo = clase?.Asignatura ?? "Clase";
        var asig = (await _context.Asignaturas.Where(a => a.CursoId == cursoId).ToListAsync()).FirstOrDefault(a => Csv.Clave(a.Nombre) == Csv.Clave(ramo));
        if (asig == null)
        {
            asig = new Asignatura { Nombre = ramo, Codigo = "", CursoId = cursoId, DocenteId = clase?.ProfesorId ?? curso.ProfesorJefeId ?? UsuarioId() };
            _context.Asignaturas.Add(asig);
            await _context.SaveChangesAsync();
        }

        var ids = await _context.Estudiantes.Where(e => e.CursoId == cursoId).Select(e => e.Id).ToListAsync();
        var fechaUtc = DiaUtc(fecha);
        var existentes = await _context.Asistencias.Where(a => ids.Contains(a.EstudianteId) && a.Fecha == fechaUtc && a.BloqueJornadaId == bloqueId).ToListAsync();
        var form = Request.Form;
        // El atraso necesita su justificativo (por qué llegó tarde)
        var sinMotivo = ids.Where(id => form[$"e_{id}"] == "2" && string.IsNullOrWhiteSpace(form[$"m_{id}"])).Count();
        if (sinMotivo > 0)
        {
            TempData["Error"] = $"Falta el motivo del atraso en {sinMotivo} {(sinMotivo == 1 ? "alumno/a" : "alumnos/as")}. No se guardó la lista.";
            return RedirectToAction(nameof(Lista), new { cursoId, bloqueId, fecha = fecha.ToString("yyyy-MM-dd") });
        }
        int aus = 0, atr = 0;
        var ausentes = new List<int>(); var atrasados = new List<int>();
        foreach (var id in ids)
        {
            var est = int.TryParse(form[$"e_{id}"], out var v) && Enum.IsDefined(typeof(EstadoAsistencia), v) ? (EstadoAsistencia)v : EstadoAsistencia.Presente;
            TimeSpan? llegada = null;
            if (est == EstadoAsistencia.Atrasado)
                llegada = TimeSpan.TryParse(form[$"h_{id}"], out var h) ? h : (bloque?.HoraInicio ?? TimeSpan.Zero);
            var r = existentes.FirstOrDefault(x => x.EstudianteId == id);
            if (r == null)
            {
                r = new Asistencia { EstudianteId = id, AsignaturaId = asig.Id, Fecha = fechaUtc, CursoId = cursoId, BloqueJornadaId = bloqueId };
                _context.Asistencias.Add(r);
            }
            r.Estado = est; r.Presente = est != EstadoAsistencia.Ausente; r.HoraLlegada = llegada; r.RegistradoPorId = UsuarioId();
            var m = form[$"m_{id}"].ToString().Trim();
            r.Observacion = est == EstadoAsistencia.Presente ? null : (m.Length > 0 ? m[..Math.Min(m.Length, 300)] : r.Observacion);
            if (finalizar && r.CerradaUtc == null) r.CerradaUtc = DateTime.UtcNow;
            if (est == EstadoAsistencia.Ausente) { aus++; ausentes.Add(id); }
            if (est == EstadoAsistencia.Atrasado) { atr++; atrasados.Add(id); }
        }
        // Clase iniciada → finalizada (si no se había iniciado con el botón, queda registrada igual)
        var fSes = DiaUtc(fecha);
        var ses = await _context.SesionesClase.FirstOrDefaultAsync(x => x.CursoId == cursoId && x.BloqueJornadaId == bloqueId && x.Fecha == fSes);
        if (ses == null && fecha == Hoy)
            _context.SesionesClase.Add(ses = new SesionClase { ColegioId = curso.ColegioId, CursoId = cursoId, BloqueJornadaId = bloqueId, Fecha = fSes, ProfesorId = UsuarioId(), InicioUtc = DateTime.UtcNow });
        if (ses != null && finalizar && ses.FinUtc == null) ses.FinUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        try { await RevisarAlertasAsync(curso, ausentes, atrasados, fecha); } catch { /* las alertas nunca impiden guardar la lista */ }

        TempData["SuccessMessage"] = $"✅ Lista de {curso.Nombre} {curso.Letra} guardada: {ids.Count - aus} presentes" + (atr > 0 ? $" ({atr} atrasados)" : "") + (aus > 0 ? $", {aus} ausentes" : "") + "."
            + (finalizar ? " 🔒 Clase finalizada: desde ahora solo la UTP puede corregirla." : "");
        return RedirectToAction(nameof(Index), new { fecha = fecha.ToString("yyyy-MM-dd") });
    }

    // ================================================================ Resumen del curso
    public sealed record ResumenAlumno(decimal? Porcentaje, int Presentes, int Ausentes, int Atrasos, int Total);

    /// <summary>Resumen anual por alumno/a. Se cuenta por día y bloque.</summary>
    private async Task<Dictionary<int, ResumenAlumno>> ResumenAsync(List<int> ids, int anio)
    {
        var ini = DiaUtc(new DateOnly(anio, 1, 1));
        var fin = DiaUtc(new DateOnly(anio + 1, 1, 1));
        return (await _context.Asistencias.AsNoTracking().Where(a => ids.Contains(a.EstudianteId) && a.Fecha >= ini && a.Fecha < fin)
                .GroupBy(a => a.EstudianteId)
                .Select(g => new { g.Key, P = g.Count(x => x.Estado != EstadoAsistencia.Ausente), A = g.Count(x => x.Estado == EstadoAsistencia.Ausente), At = g.Count(x => x.Estado == EstadoAsistencia.Atrasado), T = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Key, x => new ResumenAlumno(Porcentaje(x.P, x.T), x.P, x.A, x.At, x.T));
    }

    [HttpGet]
    public async Task<IActionResult> Curso(int id)
    {
        var curso = await _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe).FirstOrDefaultAsync(c => c.Id == id);
        if (curso == null) return NotFound();
        if (!PuedeVerPorcentaje(curso, UsuarioId()))
        {
            TempData["Error"] = "🔒 El profesor/a jefe de este curso no comparte su % de asistencia.";
            return RedirectToAction(nameof(Index));
        }
        ViewBag.PuedeCompartir = curso.ProfesorJefeId == UsuarioId() || Corrige;
        var alumnos = await _context.Estudiantes.AsNoTracking().Include(e => e.Usuario).Where(e => e.CursoId == id && e.Usuario != null && e.Usuario.Activo).ToListAsync();
        var res = await ResumenAsync(alumnos.Select(a => a.Id).ToList(), curso.AnioAcademico);
        ViewBag.Curso = curso;
        ViewBag.Alumnos = alumnos.OrderBy(a => a.Usuario!.Apellido).ThenBy(a => a.Usuario!.Nombre)
            .Select(a => (a.Id, Nombre: $"{a.Usuario!.Apellido}, {a.Usuario.Nombre}", R: res.GetValueOrDefault(a.Id))).ToList();
        var tot = res.Values.Sum(r => r.Total);
        ViewBag.PorcentajeCurso = Porcentaje(res.Values.Sum(r => r.Presentes), tot);
        // Últimos días con lista (para ver cómo va el curso)
        var desde = DiaUtc(Hoy.AddDays(-30));
        ViewBag.Dias = (await _context.Asistencias.AsNoTracking().Where(a => a.CursoId == id && a.Fecha >= desde)
                .GroupBy(a => a.Fecha).Select(g => new { g.Key, P = g.Count(x => x.Estado != EstadoAsistencia.Ausente), T = g.Count() }).ToListAsync())
            .OrderBy(x => x.Key).Select(x => (Dia: DateOnly.FromDateTime(x.Key), Porcentaje: Porcentaje(x.P, x.T) ?? 0)).ToList();
        return View();
    }

    // ================================================================ Alertas
    private static async Task<bool> YaAvisadoAsync(ApplicationDbContext db, int usuarioId, string titulo, int dias) =>
        await db.Notificaciones.AnyAsync(n => n.UsuarioId == usuarioId && n.Titulo == titulo && n.CreadaEnUtc >= DateTime.UtcNow.AddDays(-dias));

    private async Task RevisarAlertasAsync(Curso curso, List<int> ausentes, List<int> atrasados, DateOnly fecha)
    {
        var colegioId = curso.ColegioId;
        var revisar = ausentes.Concat(atrasados).Distinct().ToList();
        if (revisar.Count == 0) return;
        var res = await ResumenAsync(revisar, fecha.Year);
        var nombres = await _context.Estudiantes.AsNoTracking().Where(e => revisar.Contains(e.Id)).Select(e => new { e.Id, N = e.Usuario!.Nombre + " " + e.Usuario.Apellido }).ToDictionaryAsync(x => x.Id, x => x.N);
        var inicioMes = DiaUtc(new DateOnly(fecha.Year, fecha.Month, 1));
        var utps = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync();
        void Avisar(int u, string t, string m, string? enlace) => _context.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = u, Titulo = t, Mensaje = m, Enlace = enlace, CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });

        foreach (var id in ausentes)
        {
            if (!res.TryGetValue(id, out var r) || r.Porcentaje == null || r.Porcentaje >= Minimo || r.Total < 10) continue;
            var t = $"🔴 Asistencia bajo 85%: {nombres.GetValueOrDefault(id)}";
            if (curso.ProfesorJefeId != null && !await YaAvisadoAsync(_context, curso.ProfesorJefeId.Value, t, 14))
                Avisar(curso.ProfesorJefeId.Value, t, $"{nombres.GetValueOrDefault(id)} ({curso.Nombre} {curso.Letra}) lleva {r.Porcentaje:0.#}% de asistencia. Bajo 85% puede repetir de curso.", $"/Asistencia/Curso/{curso.Id}");
            var apos = await _context.EstudiantesApoderados.AsNoTracking().Where(x => x.EstudianteId == id && x.Apoderado != null).Select(x => x.Apoderado!.UsuarioId).ToListAsync();
            foreach (var ap in apos)
                if (!await YaAvisadoAsync(_context, ap, "🔴 Alerta de asistencia", 14))
                    Avisar(ap, "🔴 Alerta de asistencia", $"{nombres.GetValueOrDefault(id)} lleva {r.Porcentaje:0.#}% de asistencia este año. El mínimo es 85%: te sugerimos conversar con su profesor/a jefe.", null);
        }
        foreach (var id in atrasados)
        {
            var diasAtraso = await _context.Asistencias.Where(a => a.EstudianteId == id && a.Fecha >= inicioMes && a.Estado == EstadoAsistencia.Atrasado).Select(a => a.Fecha).Distinct().CountAsync();
            if (diasAtraso < 3) continue;
            var t = $"⏰ Atrasos reiterados: {nombres.GetValueOrDefault(id)}";
            foreach (var u in utps.Append(curso.ProfesorJefeId ?? 0).Where(x => x > 0).Distinct())
                if (!await YaAvisadoAsync(_context, u, t, 14))
                    Avisar(u, t, $"{nombres.GetValueOrDefault(id)} ({curso.Nombre} {curso.Letra}) suma {diasAtraso} días con atraso este mes.", $"/Asistencia/Curso/{curso.Id}");
        }
        await _context.SaveChangesAsync();
    }
}
