namespace EduNexus.Application.Entities;

public class Asignatura
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty; // Ej: "Matemáticas", "Historia"
    public string Codigo { get; set; } = string.Empty;

    // Relación con el Curso al que pertenece
    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    // Docente que imparte la asignatura
    public int DocenteId { get; set; }
    public Usuario? Docente { get; set; }

    //horarios de clases y evaluaciones
    public ICollection<HorarioClase> Horarios { get; set; } = new List<HorarioClase>();
    public ICollection<Evaluacion> Evaluaciones { get; set; } = new List<Evaluacion>();
    
}