using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// Listas que nadie pasó: 2 horas después de terminar la clase la lista se cierra sola y queda
/// "asistencia no pasada". A la UTP le llega un solo aviso por día con todas las clases que faltan
/// (se actualiza si aparecen más). La UTP puede pasarlas igual.
/// </summary>
public static class AsistenciaNoPasada
{
    public sealed record Falta(int ColegioId, int CursoId, string Curso, int BloqueId, TimeSpan Inicio, string Asignatura, string Profesor);

    /// <summary>Último día con clases: hoy si es de lunes a viernes; sábado o domingo → el viernes anterior.</summary>
    public static DateOnly UltimoDiaHabil()
    {
        var d = AsistenciaController.Hoy;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }

    /// <summary>Clases de hoy ya vencidas y sin ningún registro de asistencia (de un colegio o de todos).</summary>
    public static Task<List<Falta>> DeHoyAsync(ApplicationDbContext db, int? colegioId = null, CancellationToken ct = default) =>
        DelDiaAsync(db, AsistenciaController.Hoy, colegioId, ct);

    /// <summary>Clases de ese día ya vencidas (2 h después de terminar) y sin ningún registro de asistencia.</summary>
    public static async Task<List<Falta>> DelDiaAsync(ApplicationDbContext db, DateOnly hoy, int? colegioId = null, CancellationToken ct = default)
    {
        var dow = (int)hoy.DayOfWeek;
        if (dow < 1 || dow > 5) return new();
        var clases = await db.ClasesHorario.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Dia == dow && (colegioId == null || c.ColegioId == colegioId) && c.Curso != null && c.Curso.AnioAcademico == hoy.Year && c.Bloque != null)
            .Select(c => new
            {
                c.ColegioId, c.CursoId, Curso = c.Curso!.Nombre + " " + c.Curso.Letra, c.BloqueJornadaId, c.Bloque!.HoraInicio, c.Bloque.HoraFin, c.Asignatura,
                Profesor = c.Profesor != null ? c.Profesor.Nombre + " " + c.Profesor.Apellido : "sin profesor/a",
                Alumnos = c.Curso.Estudiantes.Count
            }).ToListAsync(ct);
        clases = clases.Where(c => c.Alumnos > 0 && AsistenciaController.Vencida(hoy, c.HoraFin)).ToList();
        if (clases.Count == 0) return new();

        var f = AsistenciaController.DiaUtc(hoy);
        var cursoIds = clases.Select(c => c.CursoId).Distinct().ToList();
        var tomadas = (await db.Asistencias.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Fecha == f && a.CursoId != null && cursoIds.Contains(a.CursoId.Value) && a.BloqueJornadaId != null)
                .Select(a => new { C = a.CursoId!.Value, B = a.BloqueJornadaId!.Value }).Distinct().ToListAsync(ct))
            .Select(x => (x.C, x.B)).ToHashSet();
        return clases.Where(c => !tomadas.Contains((c.CursoId, c.BloqueJornadaId)))
            .OrderBy(c => c.HoraInicio).ThenBy(c => Niveles.Orden(c.Curso))
            .Select(c => new Falta(c.ColegioId, c.CursoId, c.Curso.Trim(), c.BloqueJornadaId, c.HoraInicio, c.Asignatura, c.Profesor)).ToList();
    }

    /// <summary>Crea o actualiza el aviso del día a cada UTP del colegio.</summary>
    public static async Task AvisarAsync(ApplicationDbContext db, int? colegioId = null, CancellationToken ct = default)
    {
        var faltas = await DeHoyAsync(db, colegioId, ct);
        if (faltas.Count == 0) return;
        var hoy = AsistenciaController.Hoy;
        var enlace = $"/Asistencia?fecha={hoy:yyyy-MM-dd}";
        foreach (var g in faltas.GroupBy(x => x.ColegioId))
        {
            var utps = await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.ColegioId == g.Key && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync(ct);
            if (utps.Count == 0) continue;
            var n = g.Count();
            var titulo = $"⛔ Asistencia no pasada hoy: {n} {(n == 1 ? "clase" : "clases")}";
            var detalle = string.Join(" · ", g.Take(12).Select(x => $"{x.Inicio:hh\\:mm} {x.Curso} {x.Asignatura} ({x.Profesor})")) + (n > 12 ? $" · y {n - 12} más" : "");
            var mensaje = $"Estas listas se cerraron solas {AsistenciaController.HorasCierre} horas después de la clase sin que se pasara la asistencia: {detalle}. Como UTP puedes registrarlas.";
            var existentes = await db.Notificaciones.IgnoreQueryFilters()
                .Where(x => x.ColegioId == g.Key && x.Enlace == enlace && x.Titulo.StartsWith("⛔ Asistencia no pasada") && utps.Contains(x.UsuarioId)).ToListAsync(ct);
            foreach (var u in utps)
            {
                var ya = existentes.FirstOrDefault(x => x.UsuarioId == u);
                if (ya == null)
                    db.Notificaciones.Add(new Notificacion { ColegioId = g.Key, UsuarioId = u, Titulo = titulo, Mensaje = mensaje, Enlace = enlace, CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
                else if (ya.Titulo != titulo)
                {
                    // Aparecieron más clases sin lista: se actualiza el mismo aviso y vuelve a quedar sin leer
                    ya.Titulo = titulo; ya.Mensaje = mensaje; ya.Leida = false; ya.CreadaEnUtc = DateTime.UtcNow;
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Cada 15 minutos revisa las listas vencidas sin pasar y avisa a la UTP.</summary>
public class AsistenciaNoPasadaService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AsistenciaNoPasadaService> _log;

    public AsistenciaNoPasadaService(IServiceScopeFactory scopes, ILogger<AsistenciaNoPasadaService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (TaskCanceledException) { break; }
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await AsistenciaNoPasada.AvisarAsync(db, null, stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falló la revisión de asistencia no pasada.");
            }
        }
    }
}
