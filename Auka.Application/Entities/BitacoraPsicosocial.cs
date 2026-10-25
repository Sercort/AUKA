namespace Auka.Application.Entities;

public class BitacoraPsicosocial
{
    public int Id { get; set; }

    // Multitenant: se completa solo al guardar
    public int ColegioId { get; set; }

    public int EstudianteId { get; set; }
    public int ProfesionalId { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public DateTime FechaAtencion { get; set; } = DateTime.UtcNow;

    // Auditoría: quién la modificó por última vez (el equipo comparte las fichas)
    public int? ModificadoPorId { get; set; }
    public DateTime? FechaUltimaModificacion { get; set; }

    public bool IsDeleted { get; set; } = false;
}
