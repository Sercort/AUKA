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

    // ¿Usa la plataforma con su propia cuenta? Kínder a 6° básico: no (todo lo maneja su apoderado/a).
    // Se marca solo según el curso y el equipo lo puede cambiar en la ficha.
    public bool AccesoPropio { get; set; } = true;

    // Usar EstudianteApoderado en lugar de Apoderado
    public ICollection<EstudianteApoderado> Apoderados { get; set; } = new List<EstudianteApoderado>();

    public TipoMatricula TipoMatricula { get; set; } = TipoMatricula.Nuevo;
    public DateTime FechaMatricula { get; set; } = DateTime.UtcNow;

    // Especialidad técnico-profesional (3° y 4° medio TP). Vacío en científico-humanista y en los demás cursos.
    public string? Especialidad { get; set; }

    // ---- Traslado desde otro colegio (lo revisa la UTP) ----
    public string? ColegioOrigen { get; set; }
    public string? RbdOrigen { get; set; }          // RBD: número único que el Mineduc da a cada colegio
    public string? ComunaOrigen { get; set; }
    public DateOnly? FechaTraslado { get; set; }
    public string? MotivoTraslado { get; set; }
    public bool NecesidadesEducativas { get; set; } = false;   // viene con PIE o necesidades educativas especiales
    // Documentos que aún no entrega, separados por "|" (certificado de notas, informe de personalidad...). Vacío = todo entregado.
    public string? DocumentosPendientes { get; set; }

    // Campos para el registro de reasignación
    public bool FueCambiadoPorExcepcion { get; set; } = false;
    public string? MotivoCambioExcepcion { get; set; }
}
