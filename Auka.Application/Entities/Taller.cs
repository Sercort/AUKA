namespace Auka.Application.Entities;

public class Taller
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    // Deportivo, Artístico, Científico, Cultural, Tecnológico o Comunitario
    public string Categoria { get; set; } = "Deportivo";

    // Máximo de integrantes. null = sin límite
    public int? Cupo { get; set; }

    // Texto con el nombre del profesor (se completa automáticamente al crear)
    public string? DocenteCargo { get; set; }

    // Profesor vinculado a un usuario real
    public int? DocenteId { get; set; }
    public Usuario? Docente { get; set; }

    public string? Horario { get; set; }
    public int ColegioId { get; set; }
    public bool Activo { get; set; } = true;

    // Integrantes: un alumno puede estar en varios clubes
    public ICollection<InscripcionTaller> Inscripciones { get; set; } = new List<InscripcionTaller>();
}

/// <summary>Un alumno inscrito en un club o taller.</summary>
public class InscripcionTaller
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int TallerId { get; set; }
    public Taller? Taller { get; set; }

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public int? InscritoPorId { get; set; }   // quién lo inscribió (usuario)
}
