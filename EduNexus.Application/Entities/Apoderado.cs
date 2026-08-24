namespace EduNexus.Application.Entities;

public class Apoderado
{
    public int Id { get; set; }

    // Mapeo 1 a 1 con la cuenta de usuario del apoderado
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public string TelefonoContacto { get; set; } = string.Empty;

    // Relación N a M con Estudiantes (pupilos)
    public ICollection<EstudianteApoderado> Estudiantes { get; set; } = new List<EstudianteApoderado>();
}