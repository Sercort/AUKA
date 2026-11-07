namespace Auka.Web.Models;

/// <summary>Un curso dentro de las carpetas por nivel (1º Básico › A, B, C...).</summary>
public record CarpetaCurso(int Id, string Nivel, string Letra, string Url, int Alumnos, int Capacidad,
    string? Linea = null, string? Etiqueta1 = null, string? Etiqueta2 = null,
    int? Positivas = null, int? Negativas = null,   // anotaciones: se suman en la carpeta del nivel
    string? Estado = null, string? EstadoTexto = null);   // alerta de convivencia: "alerta", "atencion" o "destacado"

/// <summary>Datos para el parcial _CursosCarpetas.</summary>
public class CarpetasVm
{
    public List<CarpetaCurso> Cursos { get; set; } = new();
    public bool PuedeAgregar { get; set; }   // muestra "➕ Agregar curso" dentro de cada nivel
    public string Clave { get; set; } = "cursos";   // para recordar qué carpeta quedó abierta en cada pantalla
}
