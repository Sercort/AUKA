namespace EduNexus.Application.Entities;

public class Evaluacion
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty; // Ej: "Prueba Coef. 1 Ágebra"
    public string Contenido { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }
}