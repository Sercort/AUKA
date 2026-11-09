using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Panel de Dirección (inicio del director/a; el administrador también lo puede abrir).
/// Ordenado según el Marco para la Buena Dirección y el Liderazgo Escolar (Mineduc):
/// 1) visión estratégica → metas del año; 2) capacidades profesionales → equipo docente;
/// 3) enseñanza y aprendizaje → rendimiento y asistencia; 4) convivencia y participación → anotaciones y clubes;
/// 5) gestión de la organización → matrícula, cumplimiento y el colegio hoy.
/// Todo es de lectura: el director/a ve y decide; no edita notas ni horarios.
/// </summary>
[Authorize(Roles = "Director,SuperAdmin")]
public class DireccionController : Controller
{
    private readonly ApplicationDbContext _context;
    public DireccionController(ApplicationDbContext context) => _context = context;

    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }
    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }

    public sealed record Persona(int Id, string Nombre, string Cargo, string Estado, string Chip);
    public sealed record CursoFila(int Id, string Curso, decimal? Asistencia, decimal? Promedio, int Alumnos, int Capacidad, bool SinJefe);

    public sealed class PanelVm
    {
        public string Nombre { get; set; } = "";
        public string Colegio { get; set; } = "";
        public bool EsDirector { get; set; }
        public MetasDireccion Metas { get; set; } = new();

        // Matrícula
        public int Alumnos { get; set; }
        public int Capacidad { get; set; }
        public int Nuevos { get; set; }
        public int Traslados { get; set; }
        public int Retirados { get; set; }
        public List<(string Nivel, int N, string Color)> PorNivel { get; set; } = new();

        // Asistencia
        public decimal? AsistenciaAnio { get; set; }
        public List<(string Mes, decimal? Pct)> AsistenciaMeses { get; set; } = new();
        public int Cronicos { get; set; }                 // alumnos/as con asistencia bajo 85%
        public List<(int Id, string Nombre, string Curso, decimal Pct)> CronicosTop { get; set; } = new();
        public decimal? CumplimientoListas { get; set; }  // % de clases del mes con lista pasada
        public int ListasProgramadasMes { get; set; }
        public int ListasPasadasMes { get; set; }

        // Rendimiento
        public decimal? Promedio { get; set; }
        public List<(string Ciclo, decimal? Prom)> PromedioCiclo { get; set; } = new();
        public List<decimal> Notas { get; set; } = new();
        public int EnRiesgo { get; set; }
        public decimal? Aprobacion { get; set; }          // % de alumnos/as con promedio ≥ 4,0

        // Convivencia y participación
        public List<(string Mes, decimal? Neg, decimal? Pos)> AnotacionesMeses { get; set; } = new();
        public int NegativasMes { get; set; }
        public int PositivasMes { get; set; }
        public int CursosEnAlerta { get; set; }
        public decimal? EnClubes { get; set; }            // % de alumnos/as en algún club
        public int Clubes { get; set; }
        public int LogrosAnio { get; set; }

        // Equipo docente
        public int Docentes { get; set; }
        public int HorasAsignadas { get; set; }
        public int HorasContrato { get; set; }
        public int SinJefe { get; set; }
        public int ProfesSinNotas { get; set; }
        public int ReemplazosMes { get; set; }
        public List<(string Depto, int N)> PorDepartamento { get; set; } = new();

        // Hoy
        public int ClasesHoy { get; set; }
        public int IniciadasHoy { get; set; }
        public int AtrasosHoy { get; set; }
        public int EvaluacionesHoy { get; set; }
        public List<Persona> Directivos { get; set; } = new();

        public List<CursoFila> Cursos { get; set; } = new();
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioId = ColegioId();
        var anio = DateTime.Now.Year;
        var hoy = AsistenciaController.Hoy;
        var mesCl = System.Globalization.CultureInfo.GetCultureInfo("es-CL").DateTimeFormat;
        string Mes(int m) => mesCl.GetAbbreviatedMonthName(m).TrimEnd('.');
        decimal R1(decimal x) => Math.Round(x, 1, MidpointRounding.AwayFromZero);

        var col = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == colegioId);
        var vm = new PanelVm
        {
            Nombre = User.Identity?.Name ?? "",
            Colegio = col?.Nombre ?? "",
            EsDirector = User.IsInRole("Director"),
            Metas = MetasDireccion.Leer(col?.MetasDireccion)
        };

        // ---------- Matrícula
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.Nombre != "Pacientes")
            .Select(c => new { c.Id, c.Nombre, c.Letra, c.ProfesorJefeId, c.CapacidadMaxima }).ToListAsync();
        var cursoIds = cursos.Select(c => c.Id).ToList();
        var nomCurso = cursos.ToDictionary(c => c.Id, c => $"{c.Nombre} {c.Letra}".Trim());
        var alumnos = await _context.Estudiantes.AsNoTracking().Where(e => cursoIds.Contains(e.CursoId) && e.Usuario != null)
            .Select(e => new { e.Id, e.CursoId, e.TipoMatricula, Activo = e.Usuario!.Activo, N = e.Usuario.Nombre + " " + e.Usuario.Apellido }).ToListAsync();
        var activos = alumnos.Where(a => a.Activo).ToList();
        vm.Alumnos = activos.Count;
        vm.Capacidad = cursos.Sum(c => c.CapacidadMaxima);
        vm.Nuevos = activos.Count(a => a.TipoMatricula == TipoMatricula.Nuevo);
        vm.Traslados = activos.Count(a => a.TipoMatricula == TipoMatricula.Traslado);
        vm.Retirados = alumnos.Count(a => !a.Activo);
        vm.PorNivel = cursos.GroupBy(c => c.Nombre).OrderBy(g => Niveles.Orden(g.Key))
            .Select(g => (g.Key.Replace("Básico", "B").Replace("Medio", "M").Replace(" ", ""), activos.Count(a => g.Any(c => c.Id == a.CursoId)),
                Niveles.Orden(g.Key) >= 10 ? "linear-gradient(180deg,#9b8cff,#6c5ce7)" : "linear-gradient(180deg,#5fd3df,#1fb5c5)")).ToList();
        vm.SinJefe = cursos.Count(c => c.ProfesorJefeId == null);

        // ---------- Asistencia (año, por mes, por alumno, por curso)
        var ini = AsistenciaController.DiaUtc(new DateOnly(anio, 1, 1));
        var asis = await _context.Asistencias.AsNoTracking().Where(a => a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.Fecha >= ini)
            .Select(a => new { a.EstudianteId, C = a.CursoId!.Value, a.Fecha, a.BloqueJornadaId, Pres = a.Estado != EstadoAsistencia.Ausente }).ToListAsync();
        if (asis.Count > 0)
        {
            vm.AsistenciaAnio = R1(100m * asis.Count(a => a.Pres) / asis.Count);
            vm.AsistenciaMeses = asis.GroupBy(a => a.Fecha.Month).OrderBy(g => g.Key).Select(g => (Mes(g.Key), (decimal?)R1(100m * g.Count(x => x.Pres) / g.Count()))).ToList();
            var porAlumno = asis.GroupBy(a => a.EstudianteId).Where(g => g.Count() >= 10).Select(g => (Id: g.Key, Pct: R1(100m * g.Count(x => x.Pres) / g.Count()), Curso: g.First().C)).ToList();
            var cron = porAlumno.Where(x => x.Pct < AsistenciaController.Minimo).OrderBy(x => x.Pct).ToList();
            vm.Cronicos = cron.Count;
            var nombres = alumnos.ToDictionary(a => a.Id, a => a.N);
            vm.CronicosTop = cron.Take(12).Select(x => (x.Id, nombres.GetValueOrDefault(x.Id, "Alumno/a"), nomCurso.GetValueOrDefault(x.Curso, ""), x.Pct)).ToList();
        }
        var asisCurso = asis.GroupBy(a => a.C).ToDictionary(g => g.Key, g => (decimal?)R1(100m * g.Count(x => x.Pres) / g.Count()));

        // Cumplimiento: clases del mes (hasta ayer) que tienen su lista pasada
        var mesIni = new DateOnly(hoy.Year, hoy.Month, 1);
        var clasesSemana = await _context.ClasesHorario.AsNoTracking().Where(c => cursoIds.Contains(c.CursoId)).Select(c => new { c.CursoId, c.Dia, c.BloqueJornadaId, c.Asignatura }).ToListAsync();
        int programadas = 0;
        for (var d = mesIni; d < hoy; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) programadas += clasesSemana.Count(c => c.Dia == (int)d.DayOfWeek);
        var desdeMes = AsistenciaController.DiaUtc(mesIni);
        var hoyUtc = AsistenciaController.DiaUtc(hoy);
        var pasadas = asis.Where(a => a.Fecha >= desdeMes && a.Fecha < hoyUtc && a.BloqueJornadaId != null).Select(a => (a.C, a.BloqueJornadaId, a.Fecha)).Distinct().Count();
        vm.ListasProgramadasMes = programadas;
        vm.ListasPasadasMes = Math.Min(pasadas, programadas);
        vm.CumplimientoListas = programadas > 0 ? R1(100m * vm.ListasPasadasMes / programadas) : null;

        // ---------- Rendimiento
        var notas = await _context.Calificaciones.AsNoTracking().Where(c => c.Asignatura != null && cursoIds.Contains(c.Asignatura.CursoId))
            .Select(c => new { c.EstudianteId, c.AsignaturaId, C = c.Asignatura!.CursoId, c.Nota, Docente = c.Asignatura.DocenteId, c.Fecha }).ToListAsync();
        vm.Notas = notas.Select(n => n.Nota).ToList();
        var porRamo = notas.GroupBy(n => (n.EstudianteId, n.AsignaturaId)).Select(g => new { g.Key.EstudianteId, C = g.First().C, P = g.Average(x => x.Nota) }).ToList();
        var general = porRamo.GroupBy(x => x.EstudianteId).Select(g => new { Id = g.Key, C = g.First().C, P = g.Average(x => x.P), Rojos = g.Count(x => x.P < 4.0m) }).ToList();
        if (general.Count > 0)
        {
            vm.Promedio = R1(general.Average(g => g.P));
            vm.Aprobacion = R1(100m * general.Count(g => g.P >= 4.0m) / general.Count);
            vm.EnRiesgo = general.Count(g => g.P < 4.0m || g.Rojos >= 3);
            var cicloDe = cursos.ToDictionary(c => c.Id, c => Niveles.Ciclo(c.Nombre).Replace("Educación ", ""));
            vm.PromedioCiclo = general.GroupBy(g => cicloDe.GetValueOrDefault(g.C, "")).OrderBy(g => g.Key == "básica" ? 0 : 1)
                .Select(g => (g.Key.Length > 0 ? char.ToUpper(g.Key[0]) + g.Key[1..] : "Otros", (decimal?)R1(g.Average(x => x.P)))).ToList();
        }
        var promCurso = general.GroupBy(g => g.C).ToDictionary(g => g.Key, g => (decimal?)R1(g.Average(x => x.P)));

        // ---------- Convivencia y participación
        var anot = await _context.Anotaciones.AsNoTracking().Where(a => a.Fecha >= ini && a.Estudiante != null && cursoIds.Contains(a.Estudiante.CursoId))
            .Select(a => new { a.Tipo, a.Fecha }).ToListAsync();
        vm.AnotacionesMeses = anot.GroupBy(a => a.Fecha.Month).OrderBy(g => g.Key)
            .Select(g => (Mes(g.Key), (decimal?)g.Count(x => x.Tipo == TipoAnotacion.Negativa), (decimal?)g.Count(x => x.Tipo == TipoAnotacion.Positiva))).ToList();
        vm.NegativasMes = anot.Count(a => a.Fecha.Month == hoy.Month && a.Fecha.Year == hoy.Year && a.Tipo == TipoAnotacion.Negativa);
        vm.PositivasMes = anot.Count(a => a.Fecha.Month == hoy.Month && a.Fecha.Year == hoy.Year && a.Tipo == TipoAnotacion.Positiva);
        try { vm.CursosEnAlerta = (await Alertas.EstadoCursosAsync(_context, colegioId)).Values.Count(e => e.Nivel == Alertas.Nivel.Alerta); } catch { }
        var talleres = await _context.Talleres.AsNoTracking().Where(t => t.ColegioId == colegioId && t.Activo).Select(t => t.Id).ToListAsync();
        vm.Clubes = talleres.Count;
        var enClub = await _context.InscripcionesTaller.AsNoTracking().Where(i => talleres.Contains(i.TallerId)).Select(i => i.EstudianteId).Distinct().CountAsync();
        vm.EnClubes = vm.Alumnos > 0 ? R1(100m * enClub / vm.Alumnos) : null;
        var desdeAnio = new DateOnly(anio, 1, 1);
        vm.LogrosAnio = await _context.LogrosTaller.AsNoTracking().CountAsync(l => talleres.Contains(l.TallerId) && l.Fecha >= desdeAnio && l.Resultado != "Derrota");

        // ---------- Equipo docente
        var docentes = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
            .Select(u => new { u.Id, u.HorasContrato, u.Departamento }).ToListAsync();
        vm.Docentes = docentes.Count;
        var bloques = await _context.BloquesJornada.AsNoTracking().Where(b => b.ColegioId == colegioId).ToDictionaryAsync(b => b.Id, b => HorariosController.HorasBloque(b));
        var conProf = await _context.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorId != null).Select(c => new { P = c.ProfesorId!.Value, c.BloqueJornadaId }).ToListAsync();
        vm.HorasAsignadas = conProf.Sum(c => bloques.GetValueOrDefault(c.BloqueJornadaId, 1));
        vm.HorasContrato = docentes.Sum(d => d.HorasContrato ?? 0);
        vm.PorDepartamento = docentes.GroupBy(d => string.IsNullOrWhiteSpace(d.Departamento) ? "Sin depto." : d.Departamento!).OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count())).ToList();
        var hace30 = DateTime.UtcNow.AddDays(-30);
        var conNotas = notas.Where(n => n.Fecha >= hace30).Select(n => n.Docente).ToHashSet();
        var hacenClase = conProf.Select(c => c.P).ToHashSet();
        vm.ProfesSinNotas = docentes.Count(d => hacenClase.Contains(d.Id) && !conNotas.Contains(d.Id));
        var mesUtcIni = AsistenciaController.DiaUtc(mesIni);
        vm.ReemplazosMes = await _context.ReemplazosClase.AsNoTracking().CountAsync(r => r.ColegioId == colegioId && r.Fecha >= mesUtcIni);

        // ---------- Hoy en el colegio
        var dow = (int)hoy.DayOfWeek;
        vm.ClasesHoy = clasesSemana.Count(c => c.Dia == dow);
        vm.IniciadasHoy = (await ClasesEnCurso.DelDiaAsync(_context, colegioId, hoy)).Count;
        vm.AtrasosHoy = await _context.AtrasosLlegada.AsNoTracking().CountAsync(a => a.ColegioId == colegioId && a.Fecha == hoyUtc);
        var hoyD = DateTime.UtcNow.Date;
        vm.EvaluacionesHoy = await _context.Evaluaciones.AsNoTracking().CountAsync(e => cursoIds.Contains(e.CursoId) && e.Fecha >= hoyD && e.Fecha < hoyD.AddDays(1));
        var directivos = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo
                && (u.Rol == RolUsuario.SuperAdmin || u.Rol == RolUsuario.Director || u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Inspector || u.Rol == RolUsuario.Orientadora))
            .OrderBy(u => u.Rol).ToListAsync();
        vm.Directivos = directivos.Select(u => { var e = JornadaPersonal.Estado(u); return new Persona(u.Id, $"{u.Nombre} {u.Apellido}", HorariosController.NombreRol(u.Rol), e.Texto, e.Chip); }).ToList();

        // ---------- Cursos (tabla resumen)
        vm.Cursos = cursos.Select(c => new CursoFila(c.Id, nomCurso[c.Id], asisCurso.GetValueOrDefault(c.Id), promCurso.GetValueOrDefault(c.Id),
                activos.Count(a => a.CursoId == c.Id), c.CapacidadMaxima, c.ProfesorJefeId == null))
            .OrderBy(c => Niveles.Orden(c.Curso)).ThenBy(c => c.Curso).ToList();
        return View(vm);
    }

    /// <summary>El director/a (o el administrador) fija las metas del año.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarMetas(decimal? asistencia, decimal? promedio, int? matricula, int? negativasMes)
    {
        var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == ColegioId());
        if (col == null) return NotFound();
        var m = new MetasDireccion
        {
            Asistencia = asistencia is > 0 and <= 100 ? asistencia : null,
            Promedio = promedio is >= 1 and <= 7 ? promedio : null,
            Matricula = matricula is > 0 ? matricula : null,
            NegativasMes = negativasMes is >= 0 ? negativasMes : null
        };
        col.MetasDireccion = m.Escribir();
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "🎯 Metas del año guardadas.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Asistencia del mes en Excel (CSV): una fila por alumno/a con días registrados, presentes, ausentes, atrasos y %.
    /// Sirve para revisar y para informar (por ejemplo, a la Superintendencia o para la subvención).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportarAsistencia(int? anio, int? mes)
    {
        var colegioId = ColegioId();
        var a = anio ?? DateTime.Now.Year; var m = mes ?? DateTime.Now.Month;
        var desde = AsistenciaController.DiaUtc(new DateOnly(a, m, 1));
        var hasta = AsistenciaController.DiaUtc(new DateOnly(a, m, 1).AddMonths(1));
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == a && c.Nombre != "Pacientes").ToDictionaryAsync(c => c.Id, c => $"{c.Nombre} {c.Letra}".Trim());
        var ids = cursos.Keys.ToList();
        var reg = await _context.Asistencias.AsNoTracking().Where(x => x.CursoId != null && ids.Contains(x.CursoId.Value) && x.Fecha >= desde && x.Fecha < hasta)
            .Select(x => new { x.EstudianteId, C = x.CursoId!.Value, x.Fecha, x.Estado }).ToListAsync();
        var est = await _context.Estudiantes.AsNoTracking().Where(e => ids.Contains(e.CursoId) && e.Usuario != null)
            .Select(e => new { e.Id, e.CursoId, e.Usuario!.Rut, N = e.Usuario.Apellido + ", " + e.Usuario.Nombre }).ToListAsync();
        var filas = new List<IEnumerable<string?>> { new[] { "Curso", "RUT", "Alumno/a", "Días con lista", "Días presente", "Días ausente", "Atrasos", "% asistencia" } };
        foreach (var e in est.OrderBy(x => Niveles.Orden(cursos.GetValueOrDefault(x.CursoId))).ThenBy(x => cursos.GetValueOrDefault(x.CursoId)).ThenBy(x => x.N))
        {
            // Un día cuenta como presente si estuvo en alguna clase de ese día
            var dias = reg.Where(r => r.EstudianteId == e.Id).GroupBy(r => r.Fecha.Date).ToList();
            var pres = dias.Count(g => g.Any(r => r.Estado != EstadoAsistencia.Ausente));
            var atr = reg.Count(r => r.EstudianteId == e.Id && r.Estado == EstadoAsistencia.Atrasado);
            filas.Add(new[] { cursos.GetValueOrDefault(e.CursoId), e.Rut, e.N, dias.Count.ToString(), pres.ToString(), (dias.Count - pres).ToString(), atr.ToString(),
                dias.Count > 0 ? Math.Round(100m * pres / dias.Count, 1).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-CL")) : "" });
        }
        return File(Csv.Escribir(filas), "text/csv", $"asistencia_{a}_{m:00}.csv");
    }
}
