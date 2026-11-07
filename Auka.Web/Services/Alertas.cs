using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// Alertas de convivencia por anotaciones: avisa lo malo (curso o alumno/a en alerta) y lo bueno (curso destacado).
/// Los límites se configuran por colegio (Colegio.ConfigAlertas); por defecto:
///  - Curso en alerta: 1 negativa o más por alumno/a, o 3 alumnos/as con 3+ negativas, en los últimos 30 días.
///  - Curso en atención: las negativas se duplicaron respecto de los 30 días anteriores (mínimo 5).
///  - Curso destacado: positivas al menos el triple que las negativas y al menos 1 por cada 2 alumnos/as.
///  - Alumno/a en alerta: 3 negativas en 30 días.
/// </summary>
public static class Alertas
{
    public record Config(double NegPorAlumno, int Alumnos3, int Dias);

    public enum Nivel { Normal, Destacado, Atencion, Alerta }

    public record EstadoCurso(int CursoId, string Curso, Nivel Nivel, int Negativas, int Positivas, int Alumnos, int AlumnosCon3, string Motivo);

    public static Config Leer(string? texto)
    {
        double neg = 1; int al = 3, dias = 30;
        foreach (var p in (texto ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = p.Split('=', 2);
            if (kv.Length < 2) continue;
            var v = kv[1].Trim().Replace(',', '.');
            switch (kv[0].Trim())
            {
                case "neg": if (double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0) neg = n; break;
                case "alumnos": if (int.TryParse(v, out var a) && a > 0) al = a; break;
                case "dias": if (int.TryParse(v, out var d) && d >= 7 && d <= 120) dias = d; break;
            }
        }
        return new Config(neg, al, dias);
    }

    public static string Escribir(Config c) =>
        FormattableString.Invariant($"neg={c.NegPorAlumno};alumnos={c.Alumnos3};dias={c.Dias}");

    public static async Task<Config> ConfigAsync(ApplicationDbContext db, int colegioId) =>
        Leer(await db.Colegios.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == colegioId).Select(c => c.ConfigAlertas).FirstOrDefaultAsync());

    /// <summary>Estado de cada curso del año actual del colegio.</summary>
    public static async Task<Dictionary<int, EstadoCurso>> EstadoCursosAsync(ApplicationDbContext db, int colegioId, IEnumerable<int>? soloCursos = null)
    {
        var cfg = await ConfigAsync(db, colegioId);
        var anio = DateTime.Now.Year;
        var desde = DateTime.UtcNow.AddDays(-cfg.Dias);
        var desdeAnt = desde.AddDays(-cfg.Dias);
        var filtro = soloCursos?.ToList();

        var cursos = await db.Cursos.AsNoTracking()
            .Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.Nombre != "Pacientes" && (filtro == null || filtro.Contains(c.Id)))
            .Select(c => new { c.Id, c.Nombre, c.Letra, Alumnos = c.Estudiantes.Count })
            .ToListAsync();
        var ids = cursos.Select(c => c.Id).ToList();

        var anot = await db.Anotaciones.AsNoTracking()
            .Where(a => a.Estudiante != null && ids.Contains(a.Estudiante.CursoId) && a.Fecha >= desdeAnt)
            .Select(a => new { a.EstudianteId, CursoId = a.Estudiante!.CursoId, a.Tipo, a.Fecha })
            .ToListAsync();

        var r = new Dictionary<int, EstadoCurso>();
        foreach (var c in cursos)
        {
            var act = anot.Where(a => a.CursoId == c.Id && a.Fecha >= desde).ToList();
            var neg = act.Count(a => a.Tipo == TipoAnotacion.Negativa);
            var pos = act.Count(a => a.Tipo == TipoAnotacion.Positiva);
            var negAnt = anot.Count(a => a.CursoId == c.Id && a.Fecha < desde && a.Tipo == TipoAnotacion.Negativa);
            var con3 = act.Where(a => a.Tipo == TipoAnotacion.Negativa).GroupBy(a => a.EstudianteId).Count(g => g.Count() >= 3);
            var alumnos = Math.Max(1, c.Alumnos);
            var nombre = $"{c.Nombre} {c.Letra}".Trim();

            Nivel nivel; string motivo;
            if (neg > 0 && (neg >= cfg.NegPorAlumno * alumnos || con3 >= cfg.Alumnos3))
            {
                nivel = Nivel.Alerta;
                motivo = $"{neg} anotaciones negativas en {cfg.Dias} días" + (con3 > 0 ? $" ({con3} alumnos/as con 3 o más)" : "");
            }
            else if (neg >= 5 && neg >= negAnt * 2)
            {
                nivel = Nivel.Atencion;
                motivo = $"Las negativas subieron: {neg} en {cfg.Dias} días (antes {negAnt})";
            }
            else if (pos > 0 && pos >= neg * 3 && pos * 2 >= alumnos)
            {
                nivel = Nivel.Destacado;
                motivo = $"{pos} anotaciones positivas en {cfg.Dias} días";
            }
            else { nivel = Nivel.Normal; motivo = ""; }

            r[c.Id] = new EstadoCurso(c.Id, nombre, nivel, neg, pos, c.Alumnos, con3, motivo);
        }
        return r;
    }

    /// <summary>
    /// Después de registrar una anotación: si el curso entra en alerta, o el alumno/a llega a 3 negativas,
    /// avisa en la campana (una sola vez por período, sin repetir).
    /// </summary>
    public static async Task RevisarTrasAnotacionAsync(ApplicationDbContext db, int estudianteId)
    {
        var est = await db.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Curso == null) return;
        var colegioId = est.ColegioId;
        var cfg = await ConfigAsync(db, colegioId);
        var desde = DateTime.UtcNow.AddDays(-cfg.Dias);
        var cursoNombre = $"{est.Curso.Nombre} {est.Curso.Letra}".Trim();

        var usuarios = await db.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo).ToListAsync();
        var jefe = usuarios.Where(u => u.Id == est.Curso.ProfesorJefeId);
        var nuevos = new List<Notificacion>();

        async Task Avisar(IEnumerable<Usuario> para, string titulo, string mensaje, string enlace)
        {
            foreach (var u in para.DistinctBy(x => x.Id))
            {
                // No repetir el mismo aviso dentro del período
                var ya = await db.Notificaciones.AnyAsync(n => n.UsuarioId == u.Id && n.Titulo == titulo && n.CreadaEnUtc >= desde);
                if (ya) continue;
                nuevos.Add(new Notificacion
                {
                    ColegioId = colegioId, UsuarioId = u.Id, Titulo = titulo, Mensaje = mensaje, Enlace = enlace,
                    CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
                });
            }
        }

        // 1) Curso en alerta: profesor/a jefe, UTP, inspector/a general y director/a
        var estado = (await EstadoCursosAsync(db, colegioId, new[] { est.CursoId })).GetValueOrDefault(est.CursoId);
        if (estado?.Nivel == Nivel.Alerta)
        {
            var para = jefe.Concat(usuarios.Where(u => u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Director || u.EsInspectorGeneral));
            await Avisar(para, $"🔴 {cursoNombre} en alerta por anotaciones",
                $"{estado.Motivo}. Revisa el curso para acordar acciones.", $"/Anotaciones?cursoId={est.CursoId}");
        }

        // 2) Alumno/a con 3 negativas en el período: profesor/a jefe y orientador/a
        var negAlumno = await db.Anotaciones.CountAsync(a => a.EstudianteId == estudianteId && a.Tipo == TipoAnotacion.Negativa && a.Fecha >= desde);
        if (negAlumno >= 3 && est.Usuario != null)
        {
            var para = jefe.Concat(usuarios.Where(u => u.Rol == RolUsuario.Orientadora));
            await Avisar(para, $"⚠️ {est.Usuario.Nombre} {est.Usuario.Apellido} ({cursoNombre}) suma {negAlumno} negativas",
                $"{negAlumno} anotaciones negativas en los últimos {cfg.Dias} días. Conviene conversar con el alumno/a y su apoderado/a.",
                $"/Anotaciones?estudianteId={estudianteId}");
        }

        if (nuevos.Count > 0)
        {
            db.Notificaciones.AddRange(nuevos);
            await db.SaveChangesAsync();
        }
    }
}
