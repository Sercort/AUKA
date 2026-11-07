namespace Auka.Application.Entities;

/// <summary>
/// Bloque de la jornada del colegio (igual de lunes a viernes): clases, recreos y almuerzo.
/// Lo configura la UTP o el administrador; cada colegio tiene su propia jornada.
/// </summary>
public class BloqueJornada
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int Orden { get; set; }                 // 1, 2, 3... en orden del día
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
    public TipoBloque Tipo { get; set; } = TipoBloque.Clase;
}

public enum TipoBloque
{
    Clase = 0,
    Recreo = 1,
    Almuerzo = 2
}

/// <summary>Una clase del horario: curso + día + bloque → asignatura, profesor/a y sala.</summary>
public class ClaseHorario
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    public int Dia { get; set; }                   // 1 = lunes ... 5 = viernes
    public int BloqueJornadaId { get; set; }
    public BloqueJornada? Bloque { get; set; }

    public string Asignatura { get; set; } = string.Empty;
    public int? ProfesorId { get; set; }
    public Usuario? Profesor { get; set; }
    public string? Sala { get; set; }
}
