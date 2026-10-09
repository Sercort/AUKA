namespace Auka.Web.Models;

public class PsychosocialDashboardViewModel
{
    public string NombreProfesional { get; set; } = string.Empty;

    // KPIs superiores
    public int AtencionesDelMes { get; set; }
    public int AlertasAusentismo { get; set; }
    public int CitasPendientesSemana { get; set; }
}