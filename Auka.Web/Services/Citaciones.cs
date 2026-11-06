using Auka.Application.Entities;

namespace Auka.Web.Services;

/// <summary>Reglas compartidas de las citaciones (panel del equipo y portal de alumnos, apoderados y profesores).</summary>
public static class Citaciones
{
    // Con quién puede pedir una cita un alumno/a o un apoderado/a
    public static readonly string[] Destinatarios = { "Psicólogo/a", "Trabajador/a social", "Orientadora", "Profesor/a jefe" };

    /// <summary>Roles que atienden una solicitud según con quién se pidió (el profesor/a jefe se resuelve por el curso).</summary>
    public static RolUsuario[] RolesDe(string? destinatario) => destinatario switch
    {
        "Psicólogo/a" => new[] { RolUsuario.Psicologo, RolUsuario.Psicopedagogo },
        "Trabajador/a social" => new[] { RolUsuario.TrabajadorSocial },
        "Orientadora" => new[] { RolUsuario.Orientadora },
        _ => Array.Empty<RolUsuario>()
    };

    /// <summary>Destinatario que atiende el rol del usuario (para mostrarle sus solicitudes).</summary>
    public static string? DestinatarioDe(System.Security.Claims.ClaimsPrincipal u) =>
        u.IsInRole("Psicologo") || u.IsInRole("Psicopedagogo") ? "Psicólogo/a"
        : u.IsInRole("TrabajadorSocial") ? "Trabajador/a social"
        : u.IsInRole("Orientadora") ? "Orientadora"
        : u.IsInRole("Docente") ? "Profesor/a jefe" : null;

    public static int[] Participantes(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? Array.Empty<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).Distinct().ToArray();

    public static string Estado(EstadoCitacion e) => e switch
    {
        EstadoCitacion.Pendiente => "Por confirmar",
        EstadoCitacion.Confirmada => "Confirmada",
        EstadoCitacion.Realizada => "Asistió",
        EstadoCitacion.Cancelada => "Cancelada",
        EstadoCitacion.NoAsistio => "No asistió",
        EstadoCitacion.Rechazada => "Rechazada",
        EstadoCitacion.OtraFecha => "Propone otra fecha",
        EstadoCitacion.Solicitada => "Solicitada",
        _ => "—"
    };

    // Kínder a 6° básico: el alumno/a no usa la plataforma (todo lo maneja su apoderado/a)
    private static readonly System.Text.RegularExpressions.Regex CursoMenor = new(
        @"^\s*(pre[\s-]*k|k[ií]nder|kinder|nt\s*[12]|transici[oó]n|([1-6]\s*(°|º|o|ro|do|to|er)?|primero?|segundo|tercero?|cuarto|quinto|sexto)\s*(a[ñn]o\s*)?b[aá]sico)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static bool EsCursoMenor(string? nombreCurso) => !string.IsNullOrWhiteSpace(nombreCurso) && CursoMenor.IsMatch(nombreCurso);

    // ¿Esta citación es "del alumno/a"? (si el alumno/a no tiene cuenta, la maneja el apoderado/a)
    public static bool CitaAlAlumno(Citacion c) => c.CitadoA is "Alumno" or "Alumno + apoderado" or "Paciente";
    public static bool CitaAlApoderado(Citacion c) => c.CitadoA is "Apoderado" or "Alumno + apoderado";

    // Quién puede responder (confirmar, rechazar o proponer otra fecha) según a quién se citó.
    // accesoPropio = el alumno/a usa la plataforma; si no, responde el apoderado/a por él/ella.
    public static bool PuedeResponder(Citacion c, bool esApoderado, bool esDocente, bool accesoPropio = true) =>
        esDocente ? c.CitadoA == "Profesor jefe"
        : esApoderado ? CitaAlApoderado(c) || (!accesoPropio && CitaAlAlumno(c))
        : accesoPropio && CitaAlAlumno(c);

    // Quién la ve en el portal
    public static bool LaVe(Citacion c, bool esApoderado, bool esDocente, bool accesoPropio) =>
        esDocente ? c.CitadoA == "Profesor jefe" || c.Destinatario == "Profesor/a jefe"
        : esApoderado ? CitaAlApoderado(c) || !accesoPropio || (CitaAlAlumno(c) && c.InformarOtro) || (c.SolicitadaPorId != null && c.CitadoA == "Apoderado")
        : CitaAlAlumno(c) || (CitaAlApoderado(c) && c.InformarOtro);
}
