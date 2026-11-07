namespace Auka.Application.Entities;

public class Curso
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;   // Ej: "5° Básico", "II° Medio"
    public string Letra { get; set; } = string.Empty;    // Ej: "A" (vacía si el curso no tiene letra)
    public int AnioAcademico { get; set; } = DateTime.Now.Year;
    public int CapacidadMaxima { get; set; } = 35;

    public int ColegioId { get; set; }

    public int? ProfesorJefeId { get; set; }
    public Usuario? ProfesorJefe { get; set; }

    // Viernes propio del curso (null = como el colegio). Ej: 3º Básico A sale a las 15:30 por recuperación de Matemática.
    public TimeSpan? ViernesSalida { get; set; }
    public bool? ViernesTarde { get; set; }
    // Horario del curso bloqueado por la UTP: el automático no lo toca y no se edita hasta desbloquearlo
    public bool HorarioBloqueado { get; set; }

    public ICollection<Estudiante> Estudiantes { get; set; } = new List<Estudiante>();
}
