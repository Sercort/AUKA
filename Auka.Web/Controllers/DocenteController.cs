using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Inicio del profesor/a: quién es (sus asignaturas y su jefatura), sus clases de hoy con la lista por pasar,
/// sus próximas evaluaciones, sus cursos y su club. Ve lo de otros, pero gestiona solo lo suyo.
/// </summary>
[Authorize(Roles = "Docente")]
public class DocenteController : Controller
{
    private readonly ApplicationDbContext _context;
    public DocenteController(ApplicationDbContext context) => _context = context;

    public sealed record CursoMio(int Id, string Curso, decimal? Asistencia, decimal? Promedio, string Ramos);
    public sealed class InicioVm
    {
        public string Nombre { get; set; } = "";
        public List<string> Asignaturas { get; set; } = new();
        public string? Jefatura { get; set; }
        public int? JefaturaId { get; set; }
        public List<AsistenciaController.ClaseHoy> Hoy { get; set; } = new();
        public List<UtpController.EvalSemana> Evaluaciones { get; set; } = new();
        public List<CursoMio> Cursos { get; set; } = new();
        public List<(int Id, string Nombre, int Integrantes, string? Horario)> Clubes { get; set; } = new();
        public int HorasSemana { get; set; }
        public bool HorarioConfirmado { get; set; }
        public bool HorarioBloqueado { get; set; }
        /// <summary>Notas que puso en sus asignaturas este año (para los gráficos).</summary>
        public List<decimal> MisNotas { get; set; } = new();
        public List<(string Mes, decimal? Promedio)> TendenciaNotas { get; set; } = new();
        public decimal? PromedioMio { get; set; }
        public decimal? AsistenciaMia { get; set; }
        /// <summary>Horas pedagógicas de clase de lunes a viernes.</summary>
        public List<(string Dia, int Horas)> HorasPorDia { get; set; } = new();
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var yo);
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colegioId);
        var u = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == yo);
        if (u == null) return RedirectToAction("Index", "Home");
        var anio = DateTime.Now.Year;
        var vm = new InicioVm { Nombre = u.Nombre, HorarioBloqueado = u.HorarioBloqueado };

        var clases = await _context.ClasesHorario.AsNoTracking().Include(c => c.Curso).Include(c => c.Bloque)
            .Where(c => c.ProfesorId == yo && c.Curso != null && c.Curso.AnioAcademico == anio).ToListAsync();
        vm.HorasSemana = clases.Sum(c => HorariosController.HorasBloque(c.Bloque));   // horas pedagógicas (un bloque doble = 2)
        var nombresDia = new[] { "", "Lun", "Mar", "Mié", "Jue", "Vie" };
        vm.HorasPorDia = Enumerable.Range(1, 5).Select(d => (nombresDia[d], clases.Where(c => c.Dia == d).Sum(c => HorariosController.HorasBloque(c.Bloque)))).ToList();
        var ultimoCambio = clases.Count > 0 ? clases.Max(c => c.ModificadaUtc) : (DateTime?)null;
        vm.HorarioConfirmado = u.HorarioConfirmadoUtc != null && (ultimoCambio == null || u.HorarioConfirmadoUtc >= ultimoCambio);
        // Sus asignaturas: las que hace en el horario (o las de su ficha si aún no tiene horario)
        vm.Asignaturas = clases.Where(c => !HorariosController.EsDeJefatura(c.Asignatura)).Select(c => c.Asignatura.Trim()).Distinct().OrderBy(x => x).ToList();
        if (vm.Asignaturas.Count == 0 && !string.IsNullOrWhiteSpace(u.Asignaturas))
            vm.Asignaturas = u.Asignaturas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var jef = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.ProfesorJefeId == yo && c.AnioAcademico == anio);
        if (jef != null) { vm.Jefatura = $"{jef.Nombre} {jef.Letra}".Trim(); vm.JefaturaId = jef.Id; }

        // Clases de hoy y si ya pasó la lista
        var hoy = AsistenciaController.Hoy;
        var dow = (int)hoy.DayOfWeek;
        var fechaUtc = AsistenciaController.DiaUtc(hoy);
        var deHoy = clases.Where(c => c.Dia == dow && c.Bloque != null).OrderBy(c => c.Bloque!.HoraInicio).ToList();
        var cursoIds = clases.Select(c => c.CursoId).Distinct().ToList();
        var registros = await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.Fecha == fechaUtc)
            .GroupBy(a => new { a.CursoId, a.BloqueJornadaId }).Select(g => new { g.Key.CursoId, g.Key.BloqueJornadaId, N = g.Count() }).ToListAsync();
        var alumnos = await _context.Estudiantes.Where(e => cursoIds.Contains(e.CursoId)).GroupBy(e => e.CursoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        vm.Hoy = deHoy.Select(c => new AsistenciaController.ClaseHoy(c.CursoId, $"{c.Curso!.Nombre} {c.Curso.Letra}".Trim(), c.BloqueJornadaId, c.Bloque!.HoraInicio, c.Bloque.HoraFin, c.Asignatura,
            registros.FirstOrDefault(r => r.CursoId == c.CursoId && r.BloqueJornadaId == c.BloqueJornadaId)?.N ?? 0, alumnos.GetValueOrDefault(c.CursoId), true)).ToList();

        // Próximas evaluaciones de sus ramos en sus cursos (15 días)
        var hoyUtc = DateTime.UtcNow.Date;
        var evals = await _context.Evaluaciones.AsNoTracking().Include(e => e.Asignatura).Include(e => e.Curso)
            .Where(e => cursoIds.Contains(e.CursoId) && e.Fecha >= hoyUtc && e.Fecha < hoyUtc.AddDays(15)).OrderBy(e => e.Fecha).ToListAsync();
        var misRamos = clases.Select(c => (c.CursoId, Csv.Clave(c.Asignatura))).ToHashSet();
        vm.Evaluaciones = evals.Where(e => e.Asignatura != null && misRamos.Contains((e.CursoId, Csv.Clave(e.Asignatura.Nombre))))
            .Select(e => new UtpController.EvalSemana(e.Fecha, $"{e.Curso?.Nombre} {e.Curso?.Letra}".Trim(), e.CursoId, e.Asignatura!.Nombre, e.Titulo, e.Descripcion)).Take(8).ToList();

        // Sus cursos: asistencia del año y promedio de SUS asignaturas
        var ini = AsistenciaController.DiaUtc(new DateOnly(anio, 1, 1));
        var asis = await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.Fecha >= ini)
            .GroupBy(a => a.CursoId!.Value).Select(g => new { g.Key, P = g.Count(x => x.Estado != EstadoAsistencia.Ausente), T = g.Count() }).ToDictionaryAsync(x => x.Key, x => (x.P, x.T));
        var notas = await _context.Calificaciones.AsNoTracking().Where(c => c.Asignatura != null && cursoIds.Contains(c.Asignatura.CursoId))
            .Select(c => new { c.Asignatura!.CursoId, Ramo = c.Asignatura.Nombre, c.Nota, c.Fecha }).ToListAsync();
        var mias = notas.Where(n => misRamos.Contains((n.CursoId, Csv.Clave(n.Ramo)))).ToList();
        vm.MisNotas = mias.Select(n => n.Nota).ToList();
        vm.PromedioMio = mias.Count > 0 ? Math.Round(mias.Average(n => n.Nota), 1, MidpointRounding.AwayFromZero) : null;
        var mesesCl = System.Globalization.CultureInfo.GetCultureInfo("es-CL").DateTimeFormat;
        vm.TendenciaNotas = mias.Where(n => n.Fecha.Year == anio).GroupBy(n => n.Fecha.Month).OrderBy(g => g.Key)
            .Select(g => (mesesCl.GetAbbreviatedMonthName(g.Key).TrimEnd('.'), (decimal?)Math.Round(g.Average(x => x.Nota), 1, MidpointRounding.AwayFromZero))).ToList();
        var totP = asis.Values.Sum(v => v.P); var totT = asis.Values.Sum(v => v.T);
        vm.AsistenciaMia = AsistenciaController.Porcentaje(totP, totT);
        vm.Cursos = clases.GroupBy(c => c.CursoId).Select(g =>
        {
            var ramos = g.Where(c => !HorariosController.EsDeJefatura(c.Asignatura)).Select(c => c.Asignatura.Trim()).Distinct().ToList();
            var claves = ramos.Select(Csv.Clave).ToHashSet();
            var ns = notas.Where(n => n.CursoId == g.Key && claves.Contains(Csv.Clave(n.Ramo))).Select(n => n.Nota).ToList();
            return new CursoMio(g.Key, $"{g.First().Curso!.Nombre} {g.First().Curso!.Letra}".Trim(),
                asis.TryGetValue(g.Key, out var a) ? AsistenciaController.Porcentaje(a.P, a.T) : null,
                ns.Count > 0 ? Math.Round(ns.Average(), 1, MidpointRounding.AwayFromZero) : null, string.Join(", ", ramos));
        }).OrderBy(c => Niveles.Orden(c.Curso)).ThenBy(c => c.Curso).ToList();

        // Su club
        vm.Clubes = (await _context.Talleres.AsNoTracking().Include(t => t.Inscripciones).Where(t => t.ColegioId == colegioId && t.Activo && t.DocenteId == yo).ToListAsync())
            .Select(t => (t.Id, t.Nombre, t.Inscripciones.Count, t.Horario)).ToList();
        return View(vm);
    }
}
