namespace Auka.Application.Entities;

public class Calificacion
{
    public int Id { get; set; }
    public decimal Nota { get; set; }
    public decimal Ponderacion { get; set; } = 1.0m;
    public string? Descripcion { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int EvaluacionId { get; set; }
    public Evaluacion? Evaluacion { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }
}