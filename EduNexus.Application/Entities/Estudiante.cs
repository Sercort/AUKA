namespace EduNexus.Application.Entities;

public enum TipoMatricula
{
    Renovacion,
    Nuevo,
    Traslado
}

public class Estudiante
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    // 🟢 AQUÍ ESTÁ EL CAMBIO: Usar EstudianteApoderado en lugar de Apoderado
    public ICollection<EstudianteApoderado> Apoderados { get; set; } = new List<EstudianteApoderado>();

    public TipoMatricula TipoMatricula { get; set; } = TipoMatricula.Nuevo;
    public DateTime FechaMatricula { get; set; } = DateTime.Now;

    // Campos para el registro de reasignación
    public bool FueCambiadoPorExcepcion { get; set; } = false;
    public string? MotivoCambioExcepcion { get; set; }
}