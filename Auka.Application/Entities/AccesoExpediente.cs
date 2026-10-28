namespace Auka.Application.Entities;

/// <summary>Registro de quién abrió la ficha de un alumno/a y cuándo (auditoría de confidencialidad).</summary>
public class AccesoExpediente
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int EstudianteId { get; set; }
    public int UsuarioId { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
}
