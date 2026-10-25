namespace Auka.Application.Entities;

/// <summary>Nota del calendario psicosocial (la "pizarra"). Es compartida por todo el equipo del colegio.</summary>
public class EventoCalendario
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public DateOnly Fecha { get; set; }
    public TimeOnly? Hora { get; set; }

    public string Tipo { get; set; } = "Citación";
    public string Titulo { get; set; } = string.Empty;
    public string Responsable { get; set; } = "Equipo psicosocial";
    public bool Realizada { get; set; }

    // Auditoría: quién la creó y quién la modificó por última vez
    public int CreadoPorId { get; set; }
    public DateTime CreadoEnUtc { get; set; } = DateTime.UtcNow;
    public int? ModificadoPorId { get; set; }
    public DateTime? FechaUltimaModificacion { get; set; } // la completa solo el DbContext

    public bool IsDeleted { get; set; } = false;
}
