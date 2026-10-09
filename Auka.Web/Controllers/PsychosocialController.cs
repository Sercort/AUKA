using Auka.Infrastructure.Data;
using Auka.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auka.Web.Controllers;

[Authorize(Roles = "Psicologo,Psicopedagogo")]
public class PsychosocialController : Controller
{
    private readonly ApplicationDbContext _context;

    public PsychosocialController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Psychosocial
    [HttpGet]
    public IActionResult Index()
    {
        // Multi-colegio: siempre filtraremos por el colegio del usuario logueado
        int.TryParse(User.FindFirst("ColegioId")?.Value, out int colegioId);

        var vm = new PsychosocialDashboardViewModel
        {
            NombreProfesional = User.Identity?.Name ?? "Profesional",
            // Parte 2: reemplazamos estos 0 por consultas reales
            AtencionesDelMes = 0,
            AlertasAusentismo = 0,
            CitasPendientesSemana = 0
        };

        return View(vm);
    }
}