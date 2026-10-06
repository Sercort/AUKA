namespace Auka.Application.Entities;

public class Evaluacion
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Contenido { get; set; }
    public string? Descripcion { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public decimal Ponderacion { get; set; } = 1.0m;

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    public ICollection<Calificacion> Calificaciones { get; set; } = new List<Calificacion>();
}