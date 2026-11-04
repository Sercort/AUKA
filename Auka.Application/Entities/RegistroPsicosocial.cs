namespace Auka.Application.Entities;

/// <summary>
/// Un registro de la Planilla Psicosocial (seguimiento mensual, caso judicial, informe, visita domiciliaria,
/// derivación, plan de acción, beneficio, entrega de ropa o egreso).
/// Una sola tabla para todas las fichas: el tipo dice qué ficha es y los campos van en DatosJson (jsonb),
/// así el colegio (o la organización) puede tener fichas distintas sin crear una tabla por cada planilla.
/// </summary>
public class RegistroPsicosocial
{
    public int Id { get; set; }

    // Multitenant: se completa solo al guardar (ApplicationDbContext.SaveChangesAsync)
    public int ColegioId { get; set; }

    // Alumno/a o paciente al que pertenece (null en movimientos de inventario)
    public int? EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    // Clave de la ficha: seg, jud, inf, vd, conv, tut, plan, pro, ropa, egr
    public string Tipo { get; set; } = string.Empty;

    // Campos de la ficha en JSON: { "Mes": "Noviembre", "Tipo de acción": "Carta de compromiso", ... }
    public string DatosJson { get; set; } = "{}";

    public int CreadoPorId { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;

    // Auditoría: el equipo comparte la planilla
    public int? ModificadoPorId { get; set; }
    public DateTime? FechaUltimaModificacion { get; set; }

    public bool IsDeleted { get; set; } = false;
}
