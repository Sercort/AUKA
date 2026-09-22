namespace Auka.Application.Entities;

public class Calificacion
{
    public int Id { get; set; }
    public decimal Nota { get; set; } // Ej: 6.5 (Escala 1.0 a 7.0 en Chile)
    public decimal Ponderacion { get; set; } // Porcentaje ej: 20%
    public string Descripcion { get; set; } = string.Empty; // Ej: "Prueba 1: Álgebrá"
    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    // Relaciones
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }
}