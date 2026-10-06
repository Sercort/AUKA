namespace Auka.Application.Entities;

public enum RolUsuario
{
    SuperAdmin = 1,
    Director = 2,
    UTP = 3,
    Docente = 4,
    Inspector = 5,
    Psicologo = 6,
    Psicopedagogo = 7,
    Apoderado = 8,
    Estudiante = 9
}

public class Usuario
{
    public int Id { get; set; }
    public string Rut { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;

    // Correo oficial del colegio para entrar a AUKA
    public string Email { get; set; } = string.Empty;

    // Correo personal externo solo para recuperación en caso de olvido
    public string? EmailPersonal { get; set; }
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    public RolUsuario Rol { get; set; }
    public string? CargoInstitucional { get; set; }
    public bool EsInspectorGeneral { get; set; } = false;
    public string? Asignaturas { get; set; }

    public int ColegioId { get; set; }
    public bool Activo { get; set; } = true;

    // 🔑 Flag clave: 'true' fuerza el cambio de contraseña en el primer login
    public bool DebeCambiarPassword { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}