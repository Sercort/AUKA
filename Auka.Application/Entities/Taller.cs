namespace Auka.Application.Entities;

public class Taller
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty; // Ej: Selección de Fútbol
    public string Descripcion { get; set; } = string.Empty;
    public string Horario { get; set; } = string.Empty; // Ej: Martes y Jueves 16:00 - 17:30

    // Profesor a cargo (Relación con Usuario)
    public int DocenteCargoId { get; set; }
    public Usuario? DocenteCargo { get; set; }

    // Colegio
    public int ColegioId { get; set; }
    public Colegio? Colegio { get; set; }

    // Alumnos inscritos en el taller
    public List<Estudiante> Estudiantes { get; set; } = new();
}