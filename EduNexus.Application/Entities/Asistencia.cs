namespace EduNexus.Application.Entities;

public class Asistencia
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public bool Presente { get; set; }

    // Validación por Geolocalización (Check-in)
    public double? LatitudCapturada { get; set; }
    public double? LongitudCapturada { get; set; }
    public bool EnRangoColegio { get; set; } // Resultado de la validación espacial

    // Relaciones
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }
}