namespace EduNexus.Application.Entities;

public class ConversacionIA
{
    public int Id { get; set; }
    public string PreguntaEstudiante { get; set; } = string.Empty;
    public string RespuestaIA { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    // Trazabilidad para supervisión docente
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }
}