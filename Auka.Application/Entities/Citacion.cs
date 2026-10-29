namespace Auka.Application.Entities;

public enum EstadoCitacion
{
    Pendiente = 0,
    Confirmada = 1,
    Realizada = 2,
    Cancelada = 3
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
