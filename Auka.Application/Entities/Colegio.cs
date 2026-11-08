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

    // ---- Globalización: Auka Psicosocial no es solo para colegios ----
    // Colegio | Empresa | Instituto | Fundación | Particular. Fuera de "Colegio" se habla de pacientes y no hay cursos.
    public string TipoOrganizacion { get; set; } = "Colegio";

    // Planillas psicosociales activas, separadas por coma (seg,jud,der,vd,ben,vac,egr). null = todas.
    // Las elige el dueño de la plataforma para cada organización.
    public string? PlanillasActivas { get; set; }

    // ---- Cómo trabaja el equipo psicosocial (lo configura el dueño de la plataforma) ----
    // true = todo el equipo ve y edita la misma agenda / las mismas notas; false = cada profesional lo suyo
    public bool CalendarioCompartido { get; set; } = true;
    public bool BitacoraCompartida { get; set; } = true;
    // Fichas que solo ve su área (psicología o trabajo social), separadas por coma. null = todas compartidas.
    public string? FichasPorArea { get; set; }

    // ---- Permisos que da el administrador del colegio (Plan Premium) ----
    // Quién más, además del administrador y el director, puede crear clubes y talleres: "UTP", "Orientadora" o ambos (separados por coma).
    // null = como antes (UTP sí, orientadora no). Vacío = solo administración.
    public string? ClubesPermitidos { get; set; }

    // Todos los permisos que entrega el administrador, por acción: "clubes:Director,UTP;inscribir:UTP,InspectorGeneral;horarios:UTP;profjefe:UTP".
    // null = valores por defecto (ver Auka.Web/Services/Permisos.cs). El administrador siempre puede todo.
    public string? Permisos { get; set; }

    // Períodos de matrícula (JSON): año, desde, hasta, quiénes pueden matricular y si se cerró.
    // null = sin períodos: matricula la administración (y desde septiembre, también para el año siguiente). Ver Auka.Web/Services/Matriculas.cs
    public string? PeriodosMatricula { get; set; }
    // Profesor/a jefe que termina su ciclo (8º básico o 4º medio) y vuelve a 1º:
    // false = mantiene su letra (8º A → 1º A); true = rotación (otra letra, sin repetir hasta pasar por todas)
    public bool RotarLetraJefes { get; set; }

    // El administrador (Plan Premium) entra al resumen psicosocial: ve cantidades y estados, nunca el contenido de las fichas.
    // Lo activa solo el dueño de la plataforma cuando el colegio lo pide.
    public bool PsicosocialAdministrador { get; set; } = false;

    // Especialidades técnico-profesionales del colegio (3° y 4° medio), separadas por "|". Se van agregando al matricular o al importar.
    public string? Especialidades { get; set; }

    // ---- Horarios ----
    // Los viernes se sale a esta hora (null = 13:00). Los bloques posteriores quedan bloqueados...
    public TimeSpan? ViernesSalida { get; set; }
    // ...salvo que se active la tarde del viernes (recuperación de clases)
    public bool ViernesTardeActiva { get; set; } = false;

    // ---- Alertas de convivencia ----
    // "neg=1;alumnos=3;dias=30": curso en alerta con 1 negativa por alumno, o 3 alumnos con 3+ negativas, en 30 días. null = esos valores.
    public string? ConfigAlertas { get; set; }

    // ---- Datos comerciales (los ve solo el dueño de la plataforma) ----
    public string? ContactoNombre { get; set; }       // sostenedor/a o director/a que contrató
    public string? ContactoCorreo { get; set; }
    public string? ContactoTelefono { get; set; }
    public DateTime? FechaRegistro { get; set; }      // cuándo entró a AUKA (UTC)
    public DateTime? FechaVencimiento { get; set; }   // hasta cuándo está pagado el plan (UTC); null = sin fecha
    public int? PrecioMensual { get; set; }           // en pesos chilenos
    public string? NotasInternas { get; set; }
    public DateTime? FechaBaja { get; set; }          // si se dio de baja (los datos se conservan)
    public string? MotivoBaja { get; set; }

    // Relaciones
    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    public ICollection<Curso> Cursos { get; set; } = new List<Curso>();
}
