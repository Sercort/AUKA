namespace Auka.Application.Entities;

/// <summary>
/// En qué curso estuvo cada alumno/a cada año escolar. Permite revisar el archivo de años anteriores
/// (ej: 2025) aunque el alumno/a ya haya pasado de curso.
/// </summary>
public class MatriculaAnual
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    public int Anio { get; set; }
    public TipoMatricula Tipo { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
}
