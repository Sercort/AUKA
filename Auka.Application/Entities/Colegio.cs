namespace Auka.Application.Entities;

public class Colegio
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Rut { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    // Coordenadas para el control de asistencia GPS del establecimiento
    public double LatitudColegio { get; set; }
    public double LongitudColegio { get; set; }

    // Plan contratado: true = el colegio tiene el módulo Auka Psicosocial
    public bool PlanPsicosocialActivo { get; set; } = false;

    // true = Plan Psicosocial (solo el módulo psicosocial). false (por defecto) = Plan Premium, la plataforma completa.
    public bool SoloPsicosocial { get; set; } = false;

    // Relaciones
    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    public ICollection<Curso> Cursos { get; set; } = new List<Curso>();
}
