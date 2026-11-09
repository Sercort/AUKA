using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// "Iniciar clase": qué clases están en curso (o ya terminaron) en un día, y los reemplazos que asignó la UTP.
/// Lo usan el horario (todos lo ven), la lista de asistencia y el panel de la UTP.
/// </summary>
public static class ClasesEnCurso
{
    public sealed record Estado(int CursoId, int BloqueId, TimeSpan Inicio, bool Finalizada, string Profesor, bool Reemplazo);

    public static async Task<Dictionary<(int Curso, int Bloque), Estado>> DelDiaAsync(ApplicationDbContext db, int colegioId, DateOnly dia)
    {
        var f = AsistenciaController.DiaUtc(dia);
        var ses = await db.SesionesClase.AsNoTracking().Where(s => s.ColegioId == colegioId && s.Fecha == f).ToListAsync();
        if (ses.Count == 0) return new();
        var ids = ses.Select(s => s.ProfesorId).Distinct().ToList();
        var nombres = await db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre + " " + u.Apellido);
        return ses.GroupBy(s => (s.CursoId, s.BloqueJornadaId)).ToDictionary(g => g.Key, g =>
        {
            var s = g.OrderBy(x => x.InicioUtc).First();
            return new Estado(s.CursoId, s.BloqueJornadaId, s.InicioUtc.ToLocalTime().TimeOfDay, s.FinUtc != null, nombres.GetValueOrDefault(s.ProfesorId, ""), s.Reemplazo);
        });
    }

    /// <summary>Reemplazos de un día: (curso, bloque) → profesor/a de reemplazo.</summary>
    public static async Task<Dictionary<(int Curso, int Bloque), (int ProfesorId, string Nombre, string? Motivo)>> ReemplazosAsync(ApplicationDbContext db, int colegioId, DateOnly dia)
    {
        var f = AsistenciaController.DiaUtc(dia);
        var rs = await db.ReemplazosClase.AsNoTracking().Where(r => r.ColegioId == colegioId && r.Fecha == f).ToListAsync();
        if (rs.Count == 0) return new();
        var ids = rs.Select(r => r.ProfesorId).Distinct().ToList();
        var nombres = await db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre + " " + u.Apellido);
        return rs.GroupBy(r => (r.CursoId, r.BloqueJornadaId)).ToDictionary(g => g.Key, g => { var r = g.OrderByDescending(x => x.CreadoUtc).First(); return (r.ProfesorId, nombres.GetValueOrDefault(r.ProfesorId, ""), r.Motivo); });
    }

    /// <summary>Texto corto para mostrar: "🟢 Iniciada 09:47 · Natalia Castillo" o "✔️ Finalizada".</summary>
    public static string Texto(Estado e) => e.Finalizada ? $"✔️ Clase finalizada (inició {e.Inicio:hh\\:mm})" : $"🟢 Clase iniciada {e.Inicio:hh\\:mm}{(e.Reemplazo ? " · reemplazo" : "")}";
}
