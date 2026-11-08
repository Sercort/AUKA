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
    public static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    /// <summary>Horas después del término de la clase en que la lista se cierra sola.</summary>
    public const int HorasCierre = 2;
    /// <summary>¿Ya pasó el plazo para pasar la lista? (2 horas después de terminar la clase, o el día ya pasó)</summary>
    public static bool Vencida(DateOnly dia, TimeSpan fin) => DateTime.Now > dia.ToDateTime(TimeOnly.MinValue).Add(fin).AddHours(HorasCierre);

    /// <summary>% de asistencia (presentes + atrasados + justificados sobre el total de registros).</summary>
    public static decimal? Porcentaje(int presentes, int total) => total == 0 ? null : Math.Round(presentes * 100m / total, 1, MidpointRounding.AwayFromZero);

    public sealed record ClaseHoy(int CursoId, string Curso, int BloqueId, TimeSpan Inicio, TimeSpan Fin, string Asignatura, int Registros, int Alumnos, bool EsMia);

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
        if (!VeTodo) q = q.Where(c => c.ProfesorId == yo);
        var clases = await q.ToListAsync();
        var fechaUtc = DiaUtc(dia);
        var cursoIds = clases.Select(c => c.CursoId).Distinct().ToList();
        var registros = await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.Fecha == fechaUtc)
            .GroupBy(a => new { a.CursoId, a.BloqueJornadaId }).Select(g => new { g.Key.CursoId, g.Key.BloqueJornadaId, N = g.Count() }).ToListAsync();
        var alumnos = await _context.Estudiantes.Where(e => cursoIds.Contains(e.CursoId)).GroupBy(e => e.CursoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        ViewBag.Clases = clases
            .OrderBy(c => c.Bloque!.HoraInicio).ThenBy(c => Niveles.Orden(c.Curso!.Nombre)).ThenBy(c => c.Curso!.Letra)
            .Select(c => new ClaseHoy(c.CursoId, $"{c.Curso!.Nombre} {c.Curso.Letra}".Trim(), c.BloqueJornadaId, c.Bloque!.HoraInicio, c.Bloque.HoraFin, c.Asignatura,
                registros.FirstOrDefault(r => r.CursoId == c.CursoId && r.BloqueJornadaId == c.BloqueJornadaId)?.N ?? 0,
                alumnos.GetValueOrDefault(c.CursoId), c.ProfesorId == yo)).ToList();

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

    private Task<bool> EsSuClaseAsync(int cursoId, int bloqueId, int dow) =>
        _context.ClasesHorario.AnyAsync(c => c.CursoId == cursoId && c.BloqueJornadaId == bloqueId && c.Dia == dow && c.ProfesorId == UsuarioId());

    /// <summary>¿Puede pasar o corregir esta lista? El profesor/a de la clase mientras no la finalice; la UTP siempre.</summary>
    private async Task<bool> PuedeEditarAsync(int cursoId, int bloqueId, DateOnly dia)
    {
        if (Corrige) return true;
        if (!await EsSuClaseAsync(cursoId, bloqueId, (int)dia.DayOfWeek)) return false;
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
        var esSuya = await EsSuClaseAsync(cursoId, bloqueId, dow);
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
