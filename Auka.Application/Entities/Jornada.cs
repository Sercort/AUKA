namespace Auka.Application.Entities;

/// <summary>
/// Bloque de la jornada del colegio (igual de lunes a viernes): clases, recreos y almuerzo.
/// Lo configura la UTP o el administrador; cada colegio tiene su propia jornada.
/// </summary>
public class BloqueJornada
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int Orden { get; set; }                 // 1, 2, 3... en orden del día
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
    public TipoBloque Tipo { get; set; } = TipoBloque.Clase;
}

public enum TipoBloque
{
    Clase = 0,
    Recreo = 1,
    Almuerzo = 2
}

/// <summary>Una clase del horario: curso + día + bloque → asignatura, profesor/a y sala.</summary>
public class ClaseHorario
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    public int Dia { get; set; }                   // 1 = lunes ... 5 = viernes
    public int BloqueJornadaId { get; set; }
    public BloqueJornada? Bloque { get; set; }

    public string Asignatura { get; set; } = string.Empty;
    public int? ProfesorId { get; set; }
    public Usuario? Profesor { get; set; }
    public string? Sala { get; set; }
    public DateTime ModificadaUtc { get; set; } = DateTime.UtcNow;
}

public enum EstadoSolicitudHorario
{
    Pendiente = 0,
    Aceptada = 1,
    Rechazada = 2,
    Cancelada = 3
}

/// <summary>
/// La UTP pide a un docente con horario bloqueado que acepte mover o intercambiar uno de sus bloques
/// (por ejemplo, porque otro profesor/a lo necesita por un trámite). El docente acepta o rechaza con su motivo.
/// </summary>
public class SolicitudCambioHorario
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int CursoId { get; set; }
    public int ClaseAId { get; set; }                 // la clase que se quiere mover
    public int DiaDestino { get; set; }               // a dónde va
    public int BloqueDestinoId { get; set; }
    public int? ClaseBId { get; set; }                // la clase del docente consultado que pasaría al lugar de A (null = bloque libre)
    public int ProfesorConsultadoId { get; set; }
    public int SolicitanteId { get; set; }
    public string Resumen { get; set; } = string.Empty;      // "Matemática (lun 08:00) ↔ Lenguaje (mié 10:00) · 3º Básico A"
    public string? MotivoMostrado { get; set; }               // lo que ve el docente (motivo real o "motivos personales")
    public string? MotivoInterno { get; set; }                // el motivo real, solo para la UTP
    public EstadoSolicitudHorario Estado { get; set; } = EstadoSolicitudHorario.Pendiente;
    public string? Respuesta { get; set; }
    public DateTime CreadaUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RespondidaUtc { get; set; }
}
