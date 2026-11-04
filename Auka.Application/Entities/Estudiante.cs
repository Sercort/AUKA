namespace Auka.Application.Entities;

public enum TipoMatricula
{
    Renovacion,
    Nuevo,
    Traslado
}

public class Estudiante
{
    public int Id { get; set; }

    // Multitenant: colegio al que pertenece la alumna (se completa solo al guardar)
    public int ColegioId { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    // Necesaria para el listado de vacunación (CESFAM)
    public DateOnly? FechaNacimiento { get; set; }

    // Datos de contacto del paciente (organizaciones que no son colegio). Opcionales también en colegios.
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? CorreoContacto { get; set; }   // correo personal o de la empresa

    // Usar EstudianteApoderado en lugar de Apoderado
    public ICollection<EstudianteApoderado> Apoderados { get; set; } = new List<EstudianteApoderado>();

    public TipoMatricula TipoMatricula { get; set; } = TipoMatricula.Nuevo;
    public DateTime FechaMatricula { get; set; } = DateTime.UtcNow;

    // Campos para el registro de reasignación
    public bool FueCambiadoPorExcepcion { get; set; } = false;
    public string? MotivoCambioExcepcion { get; set; }
}
