using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// Profesores/as jefe y "quién puede hacer qué" en el horario.
/// - Cada docente tiene su ciclo (básica / media) según los cursos donde hace clases o su jefatura.
/// - 3º y 4º medio técnico-profesional: el profesor/a jefe es de la especialidad del curso.
/// - Orientación y Consejo de Curso los hace el profesor/a jefe.
/// </summary>
public static class Jefaturas
{
    public sealed class Docente
    {
        public int Id { get; init; }
        public string Nombre { get; init; } = "";
        public string? Asignaturas { get; init; }
        public string? Departamento { get; init; }
        public int? HorasContrato { get; init; }
        public HashSet<string> Ciclos { get; } = new();
        public HashSet<int> Cursos { get; } = new();
        public bool EsDeEspecialidad => (Asignaturas ?? "").Contains("Especialidad", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>¿El docente hace esa asignatura? (por lo escrito en Personal, su departamento o porque ya la hace)</summary>
    public static bool Hace(Docente d, string asignatura, ISet<int>? yaLaHacen = null)
    {
        if (yaLaHacen?.Contains(d.Id) == true) return true;
        var a = Csv.Clave(asignatura);
        var mias = Csv.Clave(d.Asignaturas);
        if (a.StartsWith("especialidad"))
        {
            var esp = a.Replace("especialidad tp -", "").Replace("especialidad tp", "").Replace("especialidad", "").Trim();
            return mias.Contains("especialidad") && (esp.Length == 0 || mias.Contains(esp));
        }
        var clave = a.Length > 6 ? a[..6] : a;   // "lenguaje y comunicacion" ≈ "lengua"
        if (mias.Contains(clave)) return true;
        var depto = Auka.Web.Controllers.HorariosController.DeptoDe(asignatura);
        return depto != null && depto != "Especialidad TP" && (d.Departamento == depto || Auka.Web.Controllers.HorariosController.DeptoDe(d.Asignaturas) == depto);
    }

    /// <summary>¿Hace clases en ese ciclo? (si aún no tiene cursos, sirve para cualquiera)</summary>
    public static bool DelCiclo(Docente d, string ciclo) => d.Ciclos.Count == 0 || d.Ciclos.Contains(ciclo);

    /// <summary>¿Puede ser profe jefe de un curso de media? Sí, si es de especialidad o hace clases en media (nunca si es solo de básica).</summary>
    public static bool PuedeMedia(Docente d) => d.EsDeEspecialidad || DelCiclo(d, "Educación media");

    /// <summary>Especialidad del curso (la que tiene la mayoría de sus alumnos/as), o null si es científico-humanista o básica.</summary>
    public static string? EspecialidadDe(Curso c, IEnumerable<Estudiante> alumnos)
    {
        if (Niveles.Orden(c.Nombre) < 10) return null;   // solo enseñanza media (3º-4º, o 1º-4º si el colegio lo tiene)
        var esp = alumnos.Where(e => e.CursoId == c.Id && !string.IsNullOrWhiteSpace(e.Especialidad))
            .GroupBy(e => e.Especialidad!.Trim()).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        return esp == null || Csv.Clave(esp).StartsWith("cientific") ? null : esp;
    }

    /// <summary>
    /// Qué se muestra al lado de la letra en los cursos de media: la especialidad ("Mecánica Automotriz")
    /// o "Científico-Humanista" en 3º y 4º medio sin especialidad. Básica y 1º-2º sin especialidad no llevan nada.
    /// </summary>
    public static async Task<Dictionary<int, string>> EtiquetasAsync(ApplicationDbContext db, IEnumerable<int> cursoIds)
    {
        var ids = cursoIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var cursos = await db.Cursos.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Nombre }).ToListAsync();
        var media = cursos.Where(c => Niveles.Orden(c.Nombre) >= 10 && Niveles.Orden(c.Nombre) < 99).ToList();
        if (media.Count == 0) return new();
        var mediaIds = media.Select(c => c.Id).ToList();
        var esp = (await db.Estudiantes.AsNoTracking().Where(e => mediaIds.Contains(e.CursoId) && e.Especialidad != null && e.Especialidad != "")
                .Select(e => new { e.CursoId, e.Especialidad }).ToListAsync())
            .GroupBy(e => e.CursoId).ToDictionary(g => g.Key, g => g.GroupBy(x => x.Especialidad!.Trim()).OrderByDescending(x => x.Count()).First().Key);
        var res = new Dictionary<int, string>();
        foreach (var c in media)
        {
            if (esp.TryGetValue(c.Id, out var e) && !Csv.Clave(e).StartsWith("cientific")) res[c.Id] = e;
            else if (Niveles.Orden(c.Nombre) >= 12 || esp.ContainsKey(c.Id)) res[c.Id] = "Científico-Humanista";
        }
        return res;
    }

    /// <summary>Nombre del curso con su especialidad: "4º Medio B · Mecánica Automotriz".</summary>
    public static async Task<string> NombreConEspecialidadAsync(ApplicationDbContext db, Curso c)
    {
        var n = $"{c.Nombre} {c.Letra}".Trim();
        var et = await EtiquetasAsync(db, new[] { c.Id });
        return et.TryGetValue(c.Id, out var e) ? $"{n} · {e}" : n;
    }

    /// <summary>Docentes activos del colegio con sus ciclos y cursos (según el horario y la jefatura).</summary>
    public static async Task<List<Docente>> DocentesAsync(ApplicationDbContext db, int colegioId, int anio)
    {
        var ds = (await db.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
                .Select(u => new { u.Id, u.Nombre, u.Apellido, u.Asignaturas, u.Departamento, u.HorasContrato }).ToListAsync())
            .Select(u => new Docente { Id = u.Id, Nombre = $"{u.Nombre} {u.Apellido}", Asignaturas = u.Asignaturas, Departamento = u.Departamento, HorasContrato = u.HorasContrato })
            .ToDictionary(d => d.Id);
        var clases = await db.ClasesHorario.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorId != null && c.Curso != null && c.Curso.AnioAcademico == anio)
            .Select(c => new { P = c.ProfesorId!.Value, c.CursoId, c.Curso!.Nombre }).Distinct().ToListAsync();
        foreach (var c in clases) if (ds.TryGetValue(c.P, out var d)) { d.Ciclos.Add(Niveles.Ciclo(c.Nombre)); d.Cursos.Add(c.CursoId); }
        var jefaturas = await db.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.ProfesorJefeId != null)
            .Select(c => new { P = c.ProfesorJefeId!.Value, c.Nombre }).ToListAsync();
        foreach (var j in jefaturas) if (ds.TryGetValue(j.P, out var d)) d.Ciclos.Add(Niveles.Ciclo(j.Nombre));
        return ds.Values.ToList();
    }

    public sealed record Resultado(int Asignados, int Corregidos, List<string> Cambios, List<string> Faltan);

    /// <summary>
    /// Completa los cursos sin profesor/a jefe y (si corregirTp) cambia a los que no corresponden (solo de básica en media, de especialidad en básica).
    /// Cursos con especialidad: primero un profe de la especialidad; si no hay, uno de media de cualquier asignatura.
    /// Prefiere a quien ya hace más horas en ese curso; Lenguaje y Matemática antes que el resto; nadie queda jefe de dos cursos.
    /// También pasa al nuevo jefe las clases de Orientación / Consejo de Curso de ese curso.
    /// </summary>
    public static async Task<Resultado> AsignarAsync(ApplicationDbContext db, int colegioId, int anio, bool corregirTp, IEnumerable<int>? soloCursos = null)
    {
        var cursos = await db.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.Nombre != "Pacientes").ToListAsync();
        if (soloCursos != null) { var s = soloCursos.ToHashSet(); cursos = cursos.Where(c => s.Contains(c.Id)).ToList(); }
        var ids = cursos.Select(c => c.Id).ToList();
        var alumnos = await db.Estudiantes.AsNoTracking().Where(e => ids.Contains(e.CursoId)).ToListAsync();
        var docentes = await DocentesAsync(db, colegioId, anio);
        var porId = docentes.ToDictionary(d => d.Id);
        var todosLosJefes = (await db.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.ProfesorJefeId != null)
            .Select(c => c.ProfesorJefeId!.Value).ToListAsync()).ToHashSet();
        var horasEnCurso = (await db.ClasesHorario.AsNoTracking().Where(c => ids.Contains(c.CursoId) && c.ProfesorId != null)
                .Select(c => new { c.CursoId, P = c.ProfesorId!.Value, c.Asignatura }).ToListAsync())
            .Where(x => !Auka.Web.Controllers.HorariosController.EsDeJefatura(x.Asignatura))
            .GroupBy(x => (x.CursoId, x.P)).ToDictionary(g => g.Key, g => g.Count());

        var cambios = new List<string>(); var faltan = new List<string>();
        int asignados = 0, corregidos = 0;
        var antes = new Dictionary<int, string>();   // curso → jefe que se cambió (para el mensaje)
        if (corregirTp)
        {
            // Solo se corrige lo que no corresponde: en media (con o sin especialidad) un profe que es solo de básica,
            // y en básica un profe de especialidad. En un curso TP, un profe de media (de cualquier asignatura) sí sirve.
            foreach (var c in cursos)
            {
                if (c.ProfesorJefeId is not int j || !porId.TryGetValue(j, out var dj)) continue;
                var media = Niveles.Orden(c.Nombre) >= 10;
                var mal = media ? !PuedeMedia(dj) : dj.EsDeEspecialidad;
                if (!mal) continue;
                antes[c.Id] = dj.Nombre;
                todosLosJefes.Remove(j);
                c.ProfesorJefeId = null;
            }
        }
        // Primero los TP (hay pocos profes de especialidad), luego media y al final básica
        foreach (var c in cursos.Where(c => c.ProfesorJefeId == null).OrderBy(c => EspecialidadDe(c, alumnos) != null ? 0 : Niveles.Orden(c.Nombre) >= 10 ? 1 : 2).ThenBy(c => Niveles.Orden(c.Nombre)).ThenBy(c => c.Letra))
        {
            var esp = EspecialidadDe(c, alumnos);
            var ciclo = Niveles.Ciclo(c.Nombre);
            var nombreCurso = $"{c.Nombre} {c.Letra}".Trim();

            // TP: primero un profe de la especialidad. Media sin especialidad: profes de media (y de especialidad si sobran). Básica: de básica.
            var media = Niveles.Orden(c.Nombre) >= 10;
            var candidatos = docentes.Where(d => !todosLosJefes.Contains(d.Id)
                    && (esp != null ? Hace(d, "Especialidad TP - " + esp) : media ? PuedeMedia(d) : !d.EsDeEspecialidad && DelCiclo(d, ciclo)))
                .OrderBy(d => esp == null && media && d.EsDeEspecialidad ? 1 : 0)
                .ThenByDescending(d => horasEnCurso.GetValueOrDefault((c.Id, d.Id)))
                .ThenBy(d => Csv.Clave(d.Asignaturas).Contains("lengua") || Csv.Clave(d.Asignaturas).Contains("matem") ? 0 : 1)
                .ThenBy(d => d.Ciclos.Contains(ciclo) ? 0 : 1)
                .ToList();
            var deMedia = false;
            if (candidatos.Count == 0 && esp != null)
            {
                // No alcanzan los profes de la especialidad: queda un profe de enseñanza media (nunca uno solo de básica),
                // primero el que ya era jefe y luego quien hace más horas en el curso
                candidatos = docentes.Where(d => !todosLosJefes.Contains(d.Id) && PuedeMedia(d))
                    .OrderBy(d => d.EsDeEspecialidad ? 1 : 0)
                    .ThenBy(d => antes.TryGetValue(c.Id, out var a) && a == d.Nombre ? 0 : 1)
                    .ThenByDescending(d => horasEnCurso.GetValueOrDefault((c.Id, d.Id)))
                    .ToList();
                deMedia = candidatos.Count > 0;
            }
            var elegido = candidatos.FirstOrDefault();
            if (elegido == null)
            {
                if (antes.TryGetValue(c.Id, out var quedo)) { var vuelve = docentes.FirstOrDefault(d => d.Nombre == quedo); if (vuelve != null && !todosLosJefes.Contains(vuelve.Id)) { c.ProfesorJefeId = vuelve.Id; todosLosJefes.Add(vuelve.Id); } }
                faltan.Add(esp != null ? $"{nombreCurso} (falta un profesor/a de {esp})" : $"{nombreCurso} (falta un profesor/a de {ciclo.Replace("Educación ", "")})");
                continue;
            }
            var nota = deMedia ? $" (profe de media: falta un profe de {esp})" : "";
            if (antes.TryGetValue(c.Id, out var anterior))
            {
                if (anterior != elegido.Nombre) { corregidos++; cambios.Add($"{nombreCurso}: {anterior} → {elegido.Nombre}{nota}"); }
            }
            else { asignados++; cambios.Add($"{nombreCurso}: {elegido.Nombre}{nota}"); }
            c.ProfesorJefeId = elegido.Id;
            todosLosJefes.Add(elegido.Id);
            elegido.Ciclos.Add(ciclo);

            // Orientación / Consejo de Curso pasan al nuevo profesor/a jefe
            foreach (var cl in await db.ClasesHorario.Where(x => x.CursoId == c.Id).ToListAsync())
                if (Auka.Web.Controllers.HorariosController.EsDeJefatura(cl.Asignatura)) { cl.ProfesorId = elegido.Id; cl.ModificadaUtc = DateTime.UtcNow; }
            if (!(antes.TryGetValue(c.Id, out var mismo) && mismo == elegido.Nombre))
            db.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = elegido.Id, Titulo = $"👩‍🏫 Eres profesor/a jefe de {nombreCurso}",
                Mensaje = $"La UTP te asignó como profesor/a jefe de {nombreCurso} ({anio}). Incluye Orientación y Consejo de Curso.", Enlace = $"/Cursos/Alumnos/{c.Id}",
                CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
        }
        await db.SaveChangesAsync();
        return new Resultado(asignados, corregidos, cambios, faltan);
    }
}
