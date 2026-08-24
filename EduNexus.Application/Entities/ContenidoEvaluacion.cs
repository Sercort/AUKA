namespace EduNexus.Application.Entities;

public class ContenidoEvaluacion
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string DetalleContenido { get; set; } = string.Empty; // Bloque donde el profe explica qué entra
    public DateTime FechaPublicacion { get; set; } = DateTime.Now;

    public int EvaluacionId { get; set; }
    public Evaluacion? Evaluacion { get; set; }

    public int DocenteId { get; set; }
    public Usuario? Docente { get; set; }
}