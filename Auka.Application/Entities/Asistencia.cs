namespace Auka.Application.Entities;

/// <summary>Cómo llegó el alumno/a a la clase. Atrasado cuenta como presente, pero queda la hora y suma atrasos.</summary>
public enum EstadoAsistencia
{
    Presente = 0,
    Ausente = 1,
    Atrasado = 2,
    Justificado = 3
}

/// <summary>
/// Atraso registrado por inspectoría en la entrada (en la mañana). Alimenta la lista de cada clase:
/// si llegó durante la clase queda "atrasado" con su hora; si llegó después de que terminó, "ausente por atraso".
/// </summary>
public class AtrasoLlegada
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }
    public DateTime Fecha { get; set; }            // día (mediodía UTC, igual que la asistencia)
    public TimeSpan Hora { get; set; }             // hora de llegada
    public string Motivo { get; set; } = string.Empty;
    public bool Justificado { get; set; }          // trae justificativo (apoderado, certificado...)
    public int RegistradoPorId { get; set; }
}

public class Asistencia
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    // Presente = vino a clases (también si llegó atrasado). Se mantiene para lo que ya existía.
    public bool Presente { get; set; }

    // Asistencia por bloque: el profesor/a pasa la lista en su clase
    public EstadoAsistencia Estado { get; set; } = EstadoAsistencia.Presente;
    public TimeSpan? HoraLlegada { get; set; }          // solo si llegó atrasado
    public int? CursoId { get; set; }
    public int? BloqueJornadaId { get; set; }
    public int? RegistradoPorId { get; set; }
    // Justificativo del atraso, o "Ausente por atraso (llegó 9:10, inspectoría)"
    public string? Observacion { get; set; }
    // El profesor/a "finalizó la clase": desde ahí solo la UTP puede corregir la lista
    public DateTime? CerradaUtc { get; set; }

    // Validación por Geolocalización (Check-in)
    public double? LatitudCapturada { get; set; }
    public double? LongitudCapturada { get; set; }
    public bool EnRangoColegio { get; set; } // Resultado de la validación espacial

    // Relaciones
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }
}

/// <summary>
/// Clase iniciada: el profesor/a (o su reemplazo) aprieta "Iniciar clase" y se abre la lista; al "Finalizar clase" se cierra.
/// Todos ven qué clases están en curso (UTP, inspectoría, alumnos/as y apoderados/as en el horario); solo la inicia quien la hace.
/// </summary>
public class SesionClase
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int CursoId { get; set; }
    public int BloqueJornadaId { get; set; }
    public DateTime Fecha { get; set; }            // día (mediodía UTC, igual que la asistencia)
    public int ProfesorId { get; set; }            // quien la inició (titular o reemplazo)
    public DateTime InicioUtc { get; set; }
    public DateTime? FinUtc { get; set; }
    public bool Reemplazo { get; set; }
}

/// <summary>La UTP asigna un profesor/a de reemplazo para una clase de un día: solo él/ella puede iniciarla y pasar la lista.</summary>
public class ReemplazoClase
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int CursoId { get; set; }
    public int BloqueJornadaId { get; set; }
    public DateTime Fecha { get; set; }            // día (mediodía UTC)
    public int ProfesorId { get; set; }            // reemplazo
    public int? TitularId { get; set; }
    public string? Motivo { get; set; }
    public int AsignadoPorId { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}
