using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Panel de la UTP (es su pantalla de inicio): lo académico del colegio en una mirada.
/// Rendimiento por curso y asignatura, alumnos/as en riesgo, profesores/as atrasados con las notas,
/// evaluaciones de la semana, convivencia (anotaciones) y pendientes de horarios y clubes.
/// El director/a y el administrador también lo pueden ver (solo lectura).
/// </summary>
[Authorize(Roles = "UTP,Director,SuperAdmin")]
public class UtpController : Controller
{
    private readonly ApplicationDbContext _context;
    public UtpController(ApplicationDbContext context) => _context = context;

    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }

    public sealed record FilaCurso(int Id, string Curso, decimal? Promedio, int Rojas, int Alumnos, int EnRiesgo);
    public sealed record FilaRamo(string Ramo, decimal Promedio, int Notas, int Rojas);
    public sealed record AlumnoRiesgo(int EstudianteId, string Nombre, string Curso, int CursoId, decimal Promedio, int RamosRojos, int Negativas);
    public sealed record ProfeAtrasado(int Id, string Nombre, string Ramos, DateTime? UltimaNota, int Dias);
    public sealed record EvalSemana(DateTime Fecha, string Curso, int CursoId, string Ramo, string Titulo, string? Tipo);

    public sealed class PanelUtpVm
    {
        public string Nombre { get; set; } = "";
        public decimal? PromedioColegio { get; set; }
        public int TotalNotas { get; set; }
        public int NotasRojas { get; set; }
        public int Alumnos { get; set; }
        public List<FilaCurso> Cursos { get; set; } = new();
        public List<FilaRamo> Ramos { get; set; } = new();
        public List<AlumnoRiesgo> Riesgo { get; set; } = new();
        public int TotalRiesgo { get; set; }
        public List<ProfeAtrasado> Atrasados { get; set; } = new();
        public List<EvalSemana> Semana { get; set; } = new();
        public List<Alertas.EstadoCurso> Convivencia { get; set; } = new();
        public int HorariosPorAprobar { get; set; }
        public int HorariosConComentario { get; set; }
        public int SolicitudesCambio { get; set; }
        public int InteresadosClubes { get; set; }
        public int CursosSinJefe { get; set; }
        public bool EsUtp { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioId = ColegioId();
        var anio = DateTime.Now.Year;
        var vm = new PanelUtpVm { Nombre = User.Identity?.Name ?? "", EsUtp = User.IsInRole("UTP") };

        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.Nombre != "Pacientes")
            .Select(c => new { c.Id, c.Nombre, c.Letra, c.ProfesorJefeId, Alumnos = c.Estudiantes.Count }).ToListAsync();
        var cursoIds = cursos.Select(c => c.Id).ToList();
        vm.CursosSinJefe = cursos.Count(c => c.ProfesorJefeId == null);
        vm.Alumnos = cursos.Sum(c => c.Alumnos);

        // ---- Notas del año (una sola consulta y todo lo demás en memoria)
        var notas = await _context.Calificaciones.AsNoTracking()
            .Where(c => c.Asignatura != null && cursoIds.Contains(c.Asignatura.CursoId))
            .Select(c => new { c.EstudianteId, c.AsignaturaId, Ramo = c.Asignatura!.Nombre, CursoId = c.Asignatura.CursoId, c.Nota, c.Fecha, Docente = c.Asignatura.DocenteId })
            .ToListAsync();
        vm.TotalNotas = notas.Count;
        vm.NotasRojas = notas.Count(n => n.Nota < 4.0m);
        decimal R(decimal x) => Math.Round(x, 1, MidpointRounding.AwayFromZero);

        // Promedio de cada alumno/a en cada ramo, y su promedio general
        var porAlumnoRamo = notas.GroupBy(n => (n.EstudianteId, n.AsignaturaId)).Select(g => new { g.Key.EstudianteId, g.First().CursoId, Prom = g.Average(x => x.Nota) }).ToList();
        var general = porAlumnoRamo.GroupBy(x => x.EstudianteId).ToDictionary(g => g.Key, g => (Prom: R(g.Average(x => x.Prom)), Rojos: g.Count(x => x.Prom < 4.0m), CursoId: g.First().CursoId));
        vm.PromedioColegio = general.Count > 0 ? R(general.Values.Average(v => v.Prom)) : null;

        // Riesgo: promedio general bajo 4,0 o 3 o más asignaturas en rojo (riesgo de repitencia)
        var riesgoIds = general.Where(g => g.Value.Prom < 4.0m || g.Value.Rojos >= 3).Select(g => g.Key).ToList();
        vm.TotalRiesgo = riesgoIds.Count;
        var desde30 = DateTime.UtcNow.AddDays(-30);
        var negativas = await _context.Anotaciones.AsNoTracking().Where(a => riesgoIds.Contains(a.EstudianteId) && a.Tipo == TipoAnotacion.Negativa && a.Fecha >= desde30)
            .GroupBy(a => a.EstudianteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var nombres = await _context.Estudiantes.AsNoTracking().Where(e => riesgoIds.Contains(e.Id))
            .Select(e => new { e.Id, N = e.Usuario!.Apellido + ", " + e.Usuario.Nombre }).ToDictionaryAsync(x => x.Id, x => x.N);
        var nomCurso = cursos.ToDictionary(c => c.Id, c => $"{c.Nombre} {c.Letra}".Trim());
        vm.Riesgo = riesgoIds.Select(id => new AlumnoRiesgo(id, nombres.GetValueOrDefault(id, "Alumno/a"), nomCurso.GetValueOrDefault(general[id].CursoId, ""), general[id].CursoId,
                general[id].Prom, general[id].Rojos, negativas.GetValueOrDefault(id)))
            .OrderBy(a => a.Promedio).ThenByDescending(a => a.RamosRojos).Take(15).ToList();

        // Por curso (de menor a mayor promedio: arriba lo que necesita atención)
        vm.Cursos = cursos.Select(c =>
        {
            var gs = general.Where(g => g.Value.CursoId == c.Id).Select(g => g.Value).ToList();
            return new FilaCurso(c.Id, nomCurso[c.Id], gs.Count > 0 ? R(gs.Average(x => x.Prom)) : null,
                notas.Count(n => n.CursoId == c.Id && n.Nota < 4.0m), c.Alumnos, gs.Count(x => x.Prom < 4.0m || x.Rojos >= 3));
        }).OrderBy(c => c.Promedio ?? 99).ThenBy(c => Niveles.Orden(c.Curso)).ToList();

        // Por asignatura (todo el colegio)
        vm.Ramos = notas.GroupBy(n => Csv.Clave(n.Ramo)).Select(g => new FilaRamo(g.First().Ramo, R(g.Average(x => x.Nota)), g.Count(), g.Count(x => x.Nota < 4.0m)))
            .OrderBy(r => r.Promedio).ToList();

        // ---- Profesores/as atrasados: hacen clases y no registran notas hace más de 30 días (o nunca)
        var hace30 = DateTime.UtcNow.AddDays(-30);
        var ultimas = notas.Where(n => n.Docente > 0).GroupBy(n => n.Docente).ToDictionary(g => g.Key, g => g.Max(x => x.Fecha));
        var clases = await _context.ClasesHorario.AsNoTracking().Where(c => c.ProfesorId != null && cursoIds.Contains(c.CursoId))
            .Select(c => new { Prof = c.ProfesorId!.Value, c.Asignatura }).ToListAsync();
        var profes = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
            .Select(u => new { u.Id, N = u.Nombre + " " + u.Apellido }).ToListAsync();
        vm.Atrasados = profes
            .Where(p => clases.Any(c => c.Prof == p.Id && !HorariosController.EsDeJefatura(c.Asignatura)))
            .Select(p =>
            {
                DateTime? ult = ultimas.TryGetValue(p.Id, out var f) ? f : null;
                var ramos = string.Join(", ", clases.Where(c => c.Prof == p.Id && !HorariosController.EsDeJefatura(c.Asignatura)).Select(c => c.Asignatura).Distinct().Take(3));
                return new ProfeAtrasado(p.Id, p.N, ramos, ult, ult == null ? 999 : (int)(DateTime.UtcNow - ult.Value).TotalDays);
            })
            .Where(p => p.UltimaNota == null || p.UltimaNota < hace30).OrderByDescending(p => p.Dias).Take(10).ToList();

        // ---- Evaluaciones de los próximos 7 días
        var hoy = DateTime.UtcNow.Date;
        vm.Semana = (await _context.Evaluaciones.AsNoTracking().Include(e => e.Asignatura)
                .Where(e => cursoIds.Contains(e.CursoId) && e.Fecha >= hoy && e.Fecha < hoy.AddDays(7)).OrderBy(e => e.Fecha).Take(40).ToListAsync())
            .Select(e => new EvalSemana(e.Fecha, nomCurso.GetValueOrDefault(e.CursoId, ""), e.CursoId, e.Asignatura?.Nombre ?? "", e.Titulo, e.Descripcion)).ToList();

        // ---- Convivencia (las mismas alertas de anotaciones)
        vm.Convivencia = (await Alertas.EstadoCursosAsync(_context, colegioId)).Values
            .Where(e => e.Nivel == Alertas.Nivel.Alerta || e.Nivel == Alertas.Nivel.Atencion || e.Nivel == Alertas.Nivel.Destacado)
            .OrderByDescending(e => e.Nivel == Alertas.Nivel.Alerta).ThenByDescending(e => e.Negativas).Take(8).ToList();

        // ---- Pendientes
        var docentes = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
            .Select(u => new { u.Id, u.HorarioConfirmadoUtc, u.HorarioBloqueado, u.HorarioObservacion }).ToListAsync();
        var conClases = clases.Select(c => c.Prof).ToHashSet();
        vm.HorariosPorAprobar = docentes.Count(d => conClases.Contains(d.Id) && !d.HorarioBloqueado && d.HorarioConfirmadoUtc != null);
        vm.HorariosConComentario = docentes.Count(d => d.HorarioObservacion != null);
        vm.SolicitudesCambio = await _context.SolicitudesCambioHorario.CountAsync(s => s.Estado == EstadoSolicitudHorario.Pendiente);
        vm.InteresadosClubes = await _context.InteresesTaller.CountAsync(i => !i.Atendido);
        return View(vm);
    }
}
