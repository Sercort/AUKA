namespace Auka.Application.Entities;

public class Taller
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    // Texto con el nombre del profesor (se completa automáticamente al crear)
    public string? DocenteCargo { get; set; }

    // Profesor vinculado a un usuario real
    public int? DocenteId { get; set; }
    public Usuario? Docente { get; set; }

    public string? Horario { get; set; }
    public int ColegioId { get; set; }
    public bool Activo { get; set; } = true;

    public ICollection<Estudiante> Estudiantes { get; set; } = new List<Estudiante>();
}