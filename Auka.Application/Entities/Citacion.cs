namespace Auka.Application.Entities;

public enum EstadoCitacion
{
    Pendiente = 0,
    Confirmada = 1,
    Realizada = 2,
    Cancelada = 3,
    NoAsistio = 4,    // la fecha pasó y la persona no vino (se marca desde la ficha)
    Rechazada = 5,    // la persona citada no puede ir y dio el motivo
    OtraFecha = 6,    // la persona citada propone otra fecha u hora
    Solicitada = 7    // la pidió el alumno/a o el apoderado/a: el equipo la agenda o la rechaza
}

/// <summary>Citación del equipo psicosocial a un alumno/a, su apoderado/a o su profesor/a jefe.</summary>
public class Citacion
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    // "Alumno", "Apoderado", "Alumno + apoderado" o "Profesor jefe"
    public string CitadoA { get; set; } = "Apoderado";

    public DateOnly Fecha { get; set; }
    public TimeOnly? Hora { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public EstadoCitacion Estado { get; set; } = EstadoCitacion.Pendiente;

    public int CreadaPorId { get; set; }
    public DateTime CreadaEnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmadaEnUtc { get; set; }
    public int? ConfirmadaPorId { get; set; }

    // Nota que la misma citación deja en el calendario compartido del equipo
    public int? EventoCalendarioId { get; set; }

    // Otros profesionales que asisten (ids separados por coma). Solo ellos reciben el aviso, además de los citados.
    public string? Participantes { get; set; }

    // Que la vea (y le llegue el aviso) también "el otro": el apoderado/a si se cita al alumno/a, o al revés.
    // Solo informa: responde quien fue citado/a.
    public bool InformarOtro { get; set; }

    // Respuesta de la persona citada: motivo del rechazo o comentario de la nueva fecha propuesta
    public string? Respuesta { get; set; }
    public DateOnly? FechaPropuesta { get; set; }
    public TimeOnly? HoraPropuesta { get; set; }

    // Cuando la cita la pide el alumno/a o el apoderado/a: con quién la pide y quién la pidió
    public string? Destinatario { get; set; }   // "Psicólogo/a", "Trabajador/a social", "Orientadora" o "Profesor/a jefe"
    public int? SolicitadaPorId { get; set; }
}

public enum EstadoEnvioCorreo
{
    Pendiente = 0,
    Enviado = 1,
    Fallido = 2,
    SinCorreo = 3,   // la persona no tiene un correo real registrado
    Simulado = 4     // el envío de correos está apagado (modo pruebas)
}

/// <summary>Aviso dentro de la plataforma. Además se intenta enviar por correo.</summary>
public class Notificacion
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int UsuarioId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Enlace { get; set; }
    public bool Leida { get; set; }
    public DateTime CreadaEnUtc { get; set; } = DateTime.UtcNow;

    // Seguimiento del correo: si falla, se reintenta solo (hasta 5 veces)
    public EstadoEnvioCorreo EstadoCorreo { get; set; } = EstadoEnvioCorreo.Pendiente;
    public string? CorreoDestino { get; set; }
    public int IntentosCorreo { get; set; }
    public string? UltimoErrorCorreo { get; set; }
}
