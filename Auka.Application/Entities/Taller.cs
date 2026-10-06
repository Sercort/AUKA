namespace Auka.Application.Entities;

public class Taller
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? DocenteCargo { get; set; }
    public string? Horario { get; set; }
    public int ColegioId { get; set; }
    public bool Activo { get; set; } = true;

    public ICollection<Estudiante> Estudiantes { get; set; } = new List<Estudiante>();
}