namespace Auka.Application.Entities;

public class Curso
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Letra { get; set; } = string.Empty;
    public int AnioAcademico { get; set; } = DateTime.Now.Year;

    // 🟢 ESTA ES LA LÍNEA QUE TE FALTA Y HACE QUE FALLE EL CONTROLADOR
    public int CapacidadMaxima { get; set; } = 35;

    public int ColegioId { get; set; }

    public int? ProfesorJefeId { get; set; }
    public Usuario? ProfesorJefe { get; set; }

    public ICollection<Estudiante> Estudiantes { get; set; } = new List<Estudiante>();
}