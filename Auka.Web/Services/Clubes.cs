using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// Reglas de horario de los clubes y talleres (van fuera del horario de clases):
///  · El horario de clases manda: un alumno/a no se inscribe si el club choca con sus clases, y el profesor/a a cargo
///    no puede tener clase a esa hora.
///  · Un lugar (cancha, gimnasio...) no lo usan dos clubes a la vez.
///  · Un profesor/a no lleva dos clubes a la vez; un alumno/a no está en dos clubes a la misma hora.
///  · Cada club es para ciertos niveles (ej. fútbol 1º a 4º y fútbol 5º a 8º) y puede tener temporada (fechas).
/// </summary>
public static class Clubes
{
    public static readonly string[] Dias = { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };

    /// <summary>Tramos para los que puede ser un club. Clave → nombre.</summary>
    public static readonly (string Clave, string Nombre)[] Tramos =
    {
        ("Parvularia", "Prekínder y kínder"),
        ("Basica1", "1º a 4º básico"),
        ("Basica2", "5º a 8º básico"),
        ("Media", "1º a 4º medio"),
        ("Adultos", "Adultos y otros cursos"),
    };

    public static string NombreTramo(string clave) => Tramos.FirstOrDefault(t => t.Clave == clave).Nombre ?? clave;

    /// <summary>Tramo de un curso según su nivel.</summary>
    public static string TramoDe(string? curso)
    {
        var o = Niveles.Orden(curso);
        return o switch
        {
            0 or 1 => "Parvularia",
            >= 2 and <= 5 => "Basica1",
            >= 6 and <= 9 => "Basica2",
            >= 10 and <= 13 => "Media",
            _ => "Adultos"
        };
    }

    public static List<string> LeerNiveles(string? txt) =>
        (txt ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Tramos.Any(t => t.Clave == x)).Distinct().ToList();

    /// <summary>Convierte un texto de planilla ("básica", "1° a 4°", "media, adultos") en claves de tramo.</summary>
    public static List<string> NivelesDesdeTexto(string? txt)
    {
        var k = Csv.Clave(txt);
        if (k.Length == 0 || k.Contains("todos")) return new();
        var r = new List<string>();
        if (k.Contains("parv") || k.Contains("kinder") || k.Contains("pre")) r.Add("Parvularia");
        if (k.Contains("1") && k.Contains("4") && k.Contains("bas") || k.Contains("basica 1") || k.Contains("primer ciclo")) r.Add("Basica1");
        if (k.Contains("5") && k.Contains("8") || k.Contains("basica 2") || k.Contains("segundo ciclo")) r.Add("Basica2");
        if (k.Contains("basica") && !r.Contains("Basica1") && !r.Contains("Basica2")) { r.Add("Basica1"); r.Add("Basica2"); }
        if (k.Contains("media")) r.Add("Media");
        if (k.Contains("adult") || k.Contains("otros")) r.Add("Adultos");
        return r.Distinct().ToList();
    }

    /// <summary>"Lunes", "lun", "1" → 1. 0 si no se entiende.</summary>
    public static int DiaDesdeTexto(string? txt)
    {
        var k = Csv.Clave(txt);
        if (int.TryParse(k, out var n) && n is >= 1 and <= 6) return n;
        if (k.Length < 2) return 0;
        for (var i = 0; i < Dias.Length; i++)
            if (Csv.Clave(Dias[i]).StartsWith(k[..Math.Min(k.Length, 3)])) return i + 1;
        return 0;
    }

    public static bool TryHora(string? txt, out TimeSpan h)
    {
        h = default;
        txt = (txt ?? "").Trim().Replace('.', ':').Replace('h', ':').TrimEnd(':');
        if (txt.Length == 0) return false;
        if (!txt.Contains(':') && int.TryParse(txt, out var solo) && solo is >= 0 and <= 23) { h = TimeSpan.FromHours(solo); return true; }
        return TimeSpan.TryParse(txt, out h) && h >= TimeSpan.Zero && h < TimeSpan.FromHours(24);
    }

    public static string Hora(TimeSpan t) => t.ToString("hh\\:mm");

    /// <summary>Texto para mostrar: "Martes y jueves 16:00–17:30 · Sábado 10:00–12:00".</summary>
    public static string Texto(IEnumerable<SesionTaller> sesiones)
    {
        var grupos = sesiones.OrderBy(s => s.Dia).GroupBy(s => (s.HoraInicio, s.HoraFin))
            .Select(g =>
            {
                var d = g.Select(s => Dias[s.Dia - 1]).ToList();
                var dias = d.Count == 1 ? d[0] : string.Join(", ", d.Take(d.Count - 1)) + " y " + d[^1].ToLower();
                dias = dias[..1] + dias[1..].ToLower();
                return $"{dias} {Hora(g.Key.HoraInicio)}–{Hora(g.Key.HoraFin)}";
            });
        return string.Join(" · ", grupos);
    }

    private static bool Cruza(TimeSpan a1, TimeSpan a2, TimeSpan b1, TimeSpan b2) => a1 < b2 && b1 < a2;

    /// <summary>¿Se cruzan las temporadas de dos clubes? (sin fechas = todo el año)</summary>
    private static bool CruzanFechas(DateOnly? i1, DateOnly? f1, DateOnly? i2, DateOnly? f2) =>
        (i1 ?? DateOnly.MinValue) <= (f2 ?? DateOnly.MaxValue) && (i2 ?? DateOnly.MinValue) <= (f1 ?? DateOnly.MaxValue);

    public static bool Terminado(Taller t) => t.FechaFin != null && t.FechaFin < DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Choques del club con otros clubes (mismo lugar o mismo profesor/a) y con las clases del profesor/a.
    /// Devuelve la lista de problemas (vacía = todo bien).
    /// </summary>
    public static async Task<List<string>> ChoquesClubAsync(ApplicationDbContext db, int colegioId, int? tallerId, int? docenteId,
        string? lugar, DateOnly? desde, DateOnly? hasta, List<SesionTaller> sesiones)
    {
        var res = new List<string>();
        for (var i = 0; i < sesiones.Count; i++)
        {
            var s = sesiones[i];
            if (s.HoraFin <= s.HoraInicio) res.Add($"{Dias[s.Dia - 1]}: la hora de término debe ser después del inicio.");
            for (var j = i + 1; j < sesiones.Count; j++)
                if (sesiones[j].Dia == s.Dia && Cruza(s.HoraInicio, s.HoraFin, sesiones[j].HoraInicio, sesiones[j].HoraFin))
                    res.Add($"{Dias[s.Dia - 1]}: hay dos horarios que se topan en el mismo club.");
        }
        if (res.Count > 0 || sesiones.Count == 0) return res;

        var otros = await db.Talleres.AsNoTracking().Include(t => t.Sesiones)
            .Where(t => t.ColegioId == colegioId && t.Activo && t.Id != (tallerId ?? 0)).ToListAsync();
        var lugarK = Csv.Clave(lugar);
        foreach (var o in otros.Where(o => CruzanFechas(desde, hasta, o.FechaInicio, o.FechaFin) && !Terminado(o)))
            foreach (var so in o.Sesiones)
                foreach (var s in sesiones.Where(s => s.Dia == so.Dia && Cruza(s.HoraInicio, s.HoraFin, so.HoraInicio, so.HoraFin)))
                {
                    var cuando = $"{Dias[s.Dia - 1].ToLower()} {Hora(so.HoraInicio)}–{Hora(so.HoraFin)}";
                    if (lugarK.Length > 0 && Csv.Clave(o.Lugar) == lugarK)
                        res.Add($"📍 {lugar} está ocupado por «{o.Nombre}» el {cuando}.");
                    if (docenteId != null && o.DocenteId == docenteId)
                        res.Add($"👩‍🏫 El profesor/a ya tiene el club «{o.Nombre}» el {cuando}.");
                }

        // El horario de clases manda: el profesor/a a cargo no puede tener clase a esa hora
        if (docenteId != null)
        {
            var clases = await db.ClasesHorario.AsNoTracking().Include(c => c.Bloque).Include(c => c.Curso)
                .Where(c => c.ProfesorId == docenteId && c.Bloque != null).ToListAsync();
            foreach (var s in sesiones)
                foreach (var c in clases.Where(c => c.Dia == s.Dia && Cruza(s.HoraInicio, s.HoraFin, c.Bloque!.HoraInicio, c.Bloque.HoraFin)))
                    res.Add($"📚 El profesor/a tiene clase de {c.Asignatura} en {c.Curso?.Nombre} {c.Curso?.Letra} el {Dias[s.Dia - 1].ToLower()} {Hora(c.Bloque!.HoraInicio)}.");
        }
        return res.Distinct().ToList();
    }

    /// <summary>
    /// ¿Puede este alumno/a estar en el club? Revisa temporada, niveles, sus clases (mandan) y sus otros clubes.
    /// Devuelve null si puede, o el motivo si no.
    /// </summary>
    public static async Task<string?> PuedeInscribirAsync(ApplicationDbContext db, Taller taller, List<SesionTaller> sesiones, Estudiante est)
    {
        if (Terminado(taller)) return "El club ya terminó su temporada.";
        var curso = est.Curso ?? await db.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == est.CursoId);
        var niveles = LeerNiveles(taller.Niveles);
        if (niveles.Count > 0 && curso != null && !niveles.Contains(TramoDe(curso.Nombre)))
            return $"Este club es para {string.Join(", ", niveles.Select(NombreTramo))}; {curso.Nombre} {curso.Letra} no corresponde.";

        if (curso != null)
        {
            var clases = await db.ClasesHorario.AsNoTracking().Include(c => c.Bloque).Where(c => c.CursoId == curso.Id && c.Bloque != null).ToListAsync();
            foreach (var s in sesiones)
            {
                var c = clases.FirstOrDefault(c => c.Dia == s.Dia && Cruza(s.HoraInicio, s.HoraFin, c.Bloque!.HoraInicio, c.Bloque.HoraFin));
                if (c != null) return $"Choca con su clase de {c.Asignatura} el {Dias[s.Dia - 1].ToLower()} a las {Hora(c.Bloque!.HoraInicio)} (las clases mandan).";
            }
        }

        // Sus otros clubes (se parte desde Talleres para poder traer las sesiones)
        var otros = await db.Talleres.AsNoTracking().Include(t => t.Sesiones)
            .Where(t => t.Id != taller.Id && t.Inscripciones.Any(i => i.EstudianteId == est.Id)).ToListAsync();
        foreach (var o in otros.Where(o => o.Activo && !Terminado(o) && CruzanFechas(taller.FechaInicio, taller.FechaFin, o.FechaInicio, o.FechaFin)))
            foreach (var so in o.Sesiones)
                if (sesiones.Any(s => s.Dia == so.Dia && Cruza(s.HoraInicio, s.HoraFin, so.HoraInicio, so.HoraFin)))
                    return $"Ya está en «{o.Nombre}» el {Dias[so.Dia - 1].ToLower()} {Hora(so.HoraInicio)}–{Hora(so.HoraFin)}.";
        return null;
    }

    /// <summary>Clubes en formato de lista para el horario (fuera del horario de clases).</summary>
    public sealed record FilaClub(int TallerId, string Club, int Dia, TimeSpan Inicio, TimeSpan Fin, string? Lugar, string? Detalle);

    public static async Task<List<FilaClub>> DelDocenteAsync(ApplicationDbContext db, int colegioId, int docenteId) =>
        (await db.Talleres.AsNoTracking().Include(t => t.Sesiones).Where(t => t.ColegioId == colegioId && t.Activo && t.DocenteId == docenteId).ToListAsync())
        .Where(t => !Terminado(t))
        .SelectMany(t => t.Sesiones.Select(s => new FilaClub(t.Id, t.Nombre, s.Dia, s.HoraInicio, s.HoraFin, t.Lugar, null)))
        .OrderBy(f => f.Dia).ThenBy(f => f.Inicio).ToList();

    public static async Task<List<FilaClub>> DeAlumnosAsync(ApplicationDbContext db, int colegioId, List<int> estudianteIds, bool conNombre)
    {
        var ins = await db.InscripcionesTaller.AsNoTracking()
            .Where(i => estudianteIds.Contains(i.EstudianteId) && i.Taller != null && i.Taller.Activo && i.Taller.ColegioId == colegioId)
            .Select(i => new { i.Taller, Nombre = i.Estudiante!.Usuario!.Nombre })
            .ToListAsync();
        var ids = ins.Select(i => i.Taller!.Id).Distinct().ToList();
        var ses = await db.SesionesTaller.AsNoTracking().Where(s => ids.Contains(s.TallerId)).ToListAsync();
        return ins.Where(i => !Terminado(i.Taller!))
            .SelectMany(i => ses.Where(s => s.TallerId == i.Taller!.Id)
                .Select(s => new FilaClub(i.Taller!.Id, i.Taller.Nombre, s.Dia, s.HoraInicio, s.HoraFin, i.Taller.Lugar, conNombre ? i.Nombre : null)))
            .OrderBy(f => f.Dia).ThenBy(f => f.Inicio).ToList();
    }

    // ================================================================ Rendimiento (las notas tienen prioridad)
    public const decimal NotaMinima = 4.0m;

    /// <summary>Promedio general del año (promedio de los promedios de cada asignatura). null si no tiene notas.</summary>
    public static async Task<Dictionary<int, decimal>> PromediosAsync(ApplicationDbContext db, List<int> estudianteIds)
    {
        var notas = await db.Calificaciones.AsNoTracking().Where(c => estudianteIds.Contains(c.EstudianteId))
            .Select(c => new { c.EstudianteId, c.AsignaturaId, c.Nota }).ToListAsync();
        return notas.GroupBy(n => n.EstudianteId).ToDictionary(g => g.Key,
            g => Math.Round(g.GroupBy(x => x.AsignaturaId).Average(a => a.Average(x => x.Nota)), 1, MidpointRounding.AwayFromZero));
    }

    private static async Task<bool> YaAvisadoAsync(ApplicationDbContext db, int usuarioId, string titulo, int dias) =>
        await db.Notificaciones.AnyAsync(n => n.UsuarioId == usuarioId && n.Titulo == titulo && n.CreadaEnUtc >= DateTime.UtcNow.AddDays(-dias));

    private static void Avisar(ApplicationDbContext db, int colegioId, int usuarioId, string titulo, string mensaje, string? enlace) =>
        db.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = usuarioId, Titulo = titulo, Mensaje = mensaje, Enlace = enlace, CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });

    /// <summary>
    /// Después de guardar notas: si un alumno/a de un club baja de 4,0 de promedio, avisa al profesor/a del club y a la UTP
    /// (y al apoderado/a solo como alerta de notas). Si alguien que salió por rendimiento ya subió, avisa que puede volver.
    /// A los compañeros/as del club nunca se les avisa. Cada aviso se repite como máximo cada 14 días.
    /// </summary>
    public static async Task RevisarRendimientoAsync(ApplicationDbContext db, int colegioId, List<int> estudianteIds)
    {
        if (estudianteIds.Count == 0) return;
        var enClub = await db.InscripcionesTaller.AsNoTracking()
            .Where(i => estudianteIds.Contains(i.EstudianteId) && i.Taller != null && i.Taller.Activo)
            .Select(i => new { i.EstudianteId, i.TallerId, Club = i.Taller!.Nombre, i.Taller.DocenteId }).ToListAsync();
        var salidas = await db.SalidasTaller.Where(x => estudianteIds.Contains(x.EstudianteId) && !x.AvisadoMejora).ToListAsync();
        var ids = enClub.Select(x => x.EstudianteId).Concat(salidas.Select(x => x.EstudianteId)).Distinct().ToList();
        if (ids.Count == 0) return;

        var prom = await PromediosAsync(db, ids);
        var alumnos = await db.Estudiantes.AsNoTracking().Include(e => e.Usuario).Where(e => ids.Contains(e.Id)).ToListAsync();
        var utp = await db.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync();

        foreach (var a in alumnos)
        {
            if (!prom.TryGetValue(a.Id, out var p)) continue;
            var nombre = $"{a.Usuario?.Nombre} {a.Usuario?.Apellido}";
            var clubes = enClub.Where(x => x.EstudianteId == a.Id).ToList();
            if (p < NotaMinima && clubes.Count > 0)
            {
                var titulo = $"📉 Rendimiento: {nombre}";
                foreach (var c in clubes)
                    if (c.DocenteId != null && !await YaAvisadoAsync(db, c.DocenteId.Value, titulo, 14))
                        Avisar(db, colegioId, c.DocenteId.Value, titulo, $"{nombre} (club «{c.Club}») tiene promedio {Csv.Nota1(p)}. Las notas tienen prioridad: conversa con la UTP si corresponde que salga del club por un tiempo.", $"/Talleres/Detalle/{c.TallerId}");
                foreach (var u in utp)
                    if (!await YaAvisadoAsync(db, u, titulo, 14))
                        Avisar(db, colegioId, u, titulo, $"{nombre} está en {string.Join(", ", clubes.Select(c => "«" + c.Club + "»"))} y tiene promedio {Csv.Nota1(p)}.", $"/Talleres/Detalle/{clubes[0].TallerId}");
                // Apoderado/a: solo alerta de notas (no se habla del club)
                var apos = await db.EstudiantesApoderados.AsNoTracking().Where(x => x.EstudianteId == a.Id && x.Apoderado != null).Select(x => x.Apoderado!.UsuarioId).ToListAsync();
                foreach (var ap in apos)
                    if (!await YaAvisadoAsync(db, ap, "📉 Alerta de notas", 14))
                        Avisar(db, colegioId, ap, "📉 Alerta de notas", $"{a.Usuario?.Nombre} tiene promedio {Csv.Nota1(p)}. Te sugerimos conversar con su profesor/a jefe.", "/Notas");
            }
            foreach (var sa in salidas.Where(x => x.EstudianteId == a.Id && p >= NotaMinima))
            {
                var club = await db.Talleres.AsNoTracking().Where(t => t.Id == sa.TallerId).Select(t => new { t.Nombre, t.DocenteId }).FirstOrDefaultAsync();
                if (club == null) { sa.AvisadoMejora = true; continue; }
                var msg = $"{nombre} subió su promedio a {Csv.Nota1(p)}: puede volver a «{club.Nombre}» si quiere (o cambiarse a otro club con cupo).";
                foreach (var u in utp.Append(club.DocenteId ?? 0).Where(x => x > 0).Distinct()) Avisar(db, colegioId, u, "📈 Subió sus notas", msg, $"/Talleres/Detalle/{sa.TallerId}");
                if (a.UsuarioId > 0) Avisar(db, colegioId, a.UsuarioId, "📈 ¡Subiste tus notas!", $"¡Bien hecho! Ya puedes volver a «{club.Nombre}» si quieres. Conversa con tu profesor/a del club o con la UTP.", "/Talleres");
                sa.AvisadoMejora = true;
            }
        }
        await db.SaveChangesAsync();
    }

    // ================================================================ Logros (trofeos y medallas)
    public static readonly (string Clave, string Icono, string Nombre, bool Gana)[] Resultados =
    {
        ("Trofeo", "🏆", "Trofeo / campeón", true), ("Oro", "🥇", "Medalla de oro", true), ("Plata", "🥈", "Medalla de plata", true),
        ("Bronce", "🥉", "Medalla de bronce", true), ("Mencion", "🎖️", "Mención o reconocimiento", true),
        ("Participacion", "🎽", "Participación", false), ("Derrota", "💪", "No ganamos esta vez", false)
    };

    public static (string Clave, string Icono, string Nombre, bool Gana) Resultado(string? r) =>
        Resultados.FirstOrDefault(x => x.Clave == r) is var x && x.Clave != null ? x : Resultados[5];

    /// <summary>Mensaje de ánimo según cómo le ha ido al club (felicita si gana; motiva si viene perdiendo).</summary>
    public static string? Animo(List<LogroTaller> logros)
    {
        if (logros.Count == 0) return null;
        var ultimo = logros.OrderByDescending(l => l.Fecha).First();
        var ganados = logros.Count(l => Resultado(l.Resultado).Gana);
        var derrotas = logros.Count(l => l.Resultado == "Derrota");
        if (Resultado(ultimo.Resultado).Gana) return $"🎉 ¡Felicitaciones por «{ultimo.Evento}»! Qué alegría, sigan así: ustedes pueden.";
        if (derrotas > ganados) return "💪 Hay forma, hay solución: revisen qué pueden mejorar y vuelvan con todo. Cada entrenamiento suma.";
        return "👏 Para la próxima: vean qué pueden mejorar y sigan así. Lo importante es participar y crecer juntos.";
    }
}
