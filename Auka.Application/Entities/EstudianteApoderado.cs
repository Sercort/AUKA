namespace Auka.Application.Entities;

public class EstudianteApoderado
{
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int ApoderadoId { get; set; }
    public Apoderado? Apoderado { get; set; }

    // Tipo de parentesco o relación (Ej: "Padre", "Madre", "Tutor Legal")
    public string Parentesco { get; set; } = string.Empty;
    public bool EsSostenedorPrincipal { get; set; } = true;
}