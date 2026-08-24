namespace EduNexus.Application.Entities;

public enum TipoAnotacion
{
    Positiva = 1,
    Negativa = 2
}

public class Anotacion
{
    public int Id { get; set; }
    public string Detalle { get; set; } = string.Empty;
    public TipoAnotacion Tipo { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    // Relaciones
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int DocenteId { get; set; }
    public Usuario? Docente { get; set; }
}