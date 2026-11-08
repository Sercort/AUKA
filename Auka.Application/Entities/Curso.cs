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
    // "Cortar": el profesor/a jefe no sigue con este curso el año siguiente (AUKA no lo asigna solo; la UTP elige a otro)
    public bool JefeNoSigue { get; set; }
    // El profesor/a jefe decide si sus colegas ven el % de asistencia del curso (la UTP y la dirección lo ven siempre)
    public bool AsistenciaVisibleColegas { get; set; } = true;

    public ICollection<Estudiante> Estudiantes { get; set; } = new List<Estudiante>();
}
