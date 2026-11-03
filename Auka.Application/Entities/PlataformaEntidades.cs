namespace Auka.Application.Entities;

public enum EstadoMensaje { Nuevo = 0, Leido = 1, Respondido = 2, Archivado = 3 }

/// <summary>Movimientos de un colegio (alta, cambio de plan, renovación, suspensión, baja). Solo lo ve el dueño de la plataforma.
/// Usa ColegioRefId (y no ColegioId) a propósito: no pertenece a ningún colegio, así que no lleva el filtro multitenant.</summary>
public class HistorialColegio
{
    public int Id { get; set; }
    public int ColegioRefId { get; set; }
    public string Tipo { get; set; } = string.Empty;      // Alta, Plan, Renovación, Suspensión, Baja, Reactivación, Datos
    public string Detalle { get; set; } = string.Empty;
    public int PorUsuarioId { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Mensajes que llegan al dueño de la plataforma: contacto, demos, sugerencias, reclamos.</summary>
public class MensajePlataforma
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "Consulta";        // Quiero contratar, Demo, Consulta, Sugerencia, Reclamo
    public string Nombre { get; set; } = string.Empty;
    public string? Cargo { get; set; }
    public string? ColegioNombre { get; set; }
    public int? ColegioRefId { get; set; }                // si el colegio ya es cliente
    public string Correo { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public EstadoMensaje Estado { get; set; } = EstadoMensaje.Nuevo;
    public string? NotaInterna { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Enlaces de un solo uso para recuperar la contraseña. Se guarda solo el hash del enlace (nunca el enlace).
/// No lleva ColegioId a propósito: se usa antes de iniciar sesión.</summary>
public class TokenRecuperacion
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiraUtc { get; set; }
    public bool Usado { get; set; } = false;
}

/// <summary>Registra cuándo corrió por última vez una tarea automática (evita avisos repetidos si la app se reinicia).</summary>
public class RegistroTarea
{
    public int Id { get; set; }
    public string Clave { get; set; } = string.Empty;
    public DateTime UltimaEjecucionUtc { get; set; }
}
