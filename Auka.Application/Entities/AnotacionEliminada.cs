namespace Auka.Application.Entities;

public class AnotacionEliminada
{
    public int Id { get; set; }
    public int AnotacionId { get; set; }
    public int EstudianteId { get; set; }
    public int UsuarioEliminoId { get; set; }
    public string MotivoEliminacion { get; set; } = string.Empty;
    public DateTime FechaEliminacion { get; set; } = DateTime.UtcNow;
    public string? ContenidoOriginal { get; set; }
    public string? TipoAnotacion { get; set; }
}