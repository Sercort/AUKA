namespace EduNexus.Application.Entities;

public enum RolUsuario
{
    SuperAdmin = 1,
    Director = 2,
    UTP = 3,
    Psicopedagogo = 4,  
    Inspector = 5,
    Docente = 6,
    Apoderado = 7,
    Estudiante = 8
}


public class Usuario
{
    public int Id { get; set; }
    public string Rut { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty; // Correo Institucional
    public string? EmailPersonal { get; set; }        // Correo personal para recuperar clave
    public string? Telefono { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }

    // 🎯 NUEVOS CAMPOS
    public string? CargoInstitucional { get; set; }  // Ej: "Jefe de UTP", "Inspector General"
    public string? Asignaturas { get; set; }         // Ej: "Matemáticas, Física, Programación"
    public bool DebeCambiarPassword { get; set; } = true; // Forzar cambio de clave preventiva

    public int ColegioId { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}