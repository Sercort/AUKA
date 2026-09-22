namespace Auka.Application.Entities;

public class HorarioClase
{
    public int Id { get; set; }
    public string Sala { get; set; } = string.Empty;

    public int CursoId { get; set; }
    public Curso? Curso { get; set; }

    public int AsignaturaId { get; set; }
    public Asignatura? Asignatura { get; set; }

    public int ProfesorId { get; set; }
    public Usuario? Profesor { get; set; }

    public int BloqueHorarioId { get; set; }
    public BloqueHorario? BloqueHorario { get; set; }
}