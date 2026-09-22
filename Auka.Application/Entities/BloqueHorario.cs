namespace Auka.Application.Entities;

public class BloqueHorario
{
    public int Id { get; set; }
    public DayOfWeek DiaSemana { get; set; } // Monday (1) a Friday (5)
    public TimeSpan HoraInicio { get; set; } // Ej: 08:00
    public TimeSpan HoraFin { get; set; }    // Ej: 08:45
    public int NumeroBloque { get; set; }    // 1, 2, 3...
    public bool EsRecreo { get; set; } = false;
}