namespace Auka.Application.Entities;

public enum EstadoEntrevista
{
    Pendiente = 1,
    Aprobada = 2,
    Rechazada = 3
}

public class SolicitudEntrevista
{
    public int Id { get; set; }
    public DateTime FechaHoraPropuesta { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public EstadoEntrevista Estado { get; set; } = EstadoEntrevista.Pendiente;
    public string? RespuestaDocente { get; set; }

    // Relaciones
    public int ApoderadoId { get; set; }
    public Apoderado? Apoderado { get; set; }

    public int DocenteId { get; set; }
    public Usuario? Docente { get; set; }
}