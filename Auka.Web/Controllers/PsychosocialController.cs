using Auka.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auka.Web.Controllers;

[Authorize(Policy = "EquipoPsicosocial")]
public class PsychosocialController : Controller
{
    // GET: /Psychosocial
    [HttpGet]
    public IActionResult Index()
    {
        var modelo = new PsychosocialDashboardViewModel
        {
            NombreProfesional = User.Identity?.Name ?? "Profesional"
        };

        return View(modelo);
    }
}