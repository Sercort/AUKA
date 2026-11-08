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