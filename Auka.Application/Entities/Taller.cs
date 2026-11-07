namespace Auka.Application.Entities;

public class Taller
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    // Deportivo, Artístico, Científico, Cultural, Tecnológico o Comunitario
    public string Categoria { get; set; } = "Deportivo";

    // Máximo de integrantes. null = sin límite
    public int? Cupo { get; set; }
    // Mínimo para funcionar (o para abrir un grupo nuevo con los interesados). null = sin mínimo
    public int? CupoMinimo { get; set; }

    // Texto con el nombre del profesor (se completa automáticamente al crear)
    public string? DocenteCargo { get; set; }

    // Profesor vinculado a un usuario real
    public int? DocenteId { get; set; }
    public Usuario? Docente { get; set; }

    // Texto que ven todos ("Martes y jueves 16:00–17:30"); se arma solo desde las sesiones
    public string? Horario { get; set; }
    public int ColegioId { get; set; }
    public bool Activo { get; set; } = true;

    // Dónde se hace (cancha, gimnasio, sala 12...): dos clubes no pueden usar el mismo lugar a la misma hora
    public string? Lugar { get; set; }
    // Para quiénes es: "Parvularia,Basica1,Basica2,Media,Adultos" (vacío = todos). Ver Auka.Web/Services/Clubes.cs
    public string? Niveles { get; set; }
    // Temporada del club (vacío = todo el año)
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaFin { get; set; }

    // Días y horas del club (fuera del horario de clases)
    public ICollection<SesionTaller> Sesiones { get; set; } = new List<SesionTaller>();

    // Integrantes: un alumno puede estar en varios clubes
    public ICollection<InscripcionTaller> Inscripciones { get; set; } = new List<InscripcionTaller>();
}

/// <summary>Un día y horario del club (ej. martes 16:00 a 17:30). Un club puede tener varios.</summary>
public class SesionTaller
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int TallerId { get; set; }
    public Taller? Taller { get; set; }
    public int Dia { get; set; }              // 1 = lunes ... 6 = sábado
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
}

/// <summary>Alumno/a interesado en un club (sin cupo, no es de su nivel, o un club que aún no existe). Le llega a la UTP.</summary>
public class InteresTaller
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }
    public int? TallerId { get; set; }            // null = un club que no existe todavía
    public string? Categoria { get; set; }        // para los que piden algo nuevo
    public string? Comentario { get; set; }
    public int RegistradoPorId { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public bool Atendido { get; set; }
}

/// <summary>Salida de un club por rendimiento (notas). Sirve para avisar cuando el alumno/a mejora y puede volver.</summary>
public class SalidaTaller
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int TallerId { get; set; }
    public int EstudianteId { get; set; }
    public string Motivo { get; set; } = "Rendimiento";
    public decimal? Promedio { get; set; }
    public int PorId { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public bool AvisadoMejora { get; set; }
}

/// <summary>Logro del club: trofeo, medalla, participación... (las derrotas solo se usan para dar ánimo, no se exhiben).</summary>
public class LogroTaller
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int TallerId { get; set; }
    public DateOnly Fecha { get; set; }
    public string Evento { get; set; } = string.Empty;       // "Campeonato comunal sub-12"
    public string Resultado { get; set; } = "Participacion"; // Trofeo, Oro, Plata, Bronce, Participacion, Derrota
    public string? Detalle { get; set; }
    public int RegistradoPorId { get; set; }
}

/// <summary>Un alumno inscrito en un club o taller.</summary>
public class InscripcionTaller
{
    public int Id { get; set; }
    public int ColegioId { get; set; }

    public int TallerId { get; set; }
    public Taller? Taller { get; set; }

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public int? InscritoPorId { get; set; }   // quién lo inscribió (usuario)
}
