namespace Auka.Application.Entities;

public class BitacoraPsicosocial
{
    public int Id { get; set; }
    public int EstudianteId { get; set; }
    public int ProfesionalId { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public DateTime FechaAtencion { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
}