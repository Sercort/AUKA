using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize]
public class TalleresController : Controller
{
    private readonly ApplicationDbContext _context;

    public TalleresController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Vista General de Talleres / Clubes
    public async Task<IActionResult> Index()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int userId);
        var rol = User.FindFirst(ClaimTypes.Role)?.Value;

        var query = _context.Talleres
            .Include(t => t.DocenteCargo)
            .Include(t => t.Estudiantes)
            .AsQueryable();

        // Si es docente, podemos filtrar opcionalmente los que tiene a cargo
        var talleres = await query.ToListAsync();

        ViewBag.Rol = rol;
        ViewBag.UserId = userId;

        return View(talleres);
    }

    // 2. Crear Taller (Exclusivo SuperAdmin y Director)
    [Authorize(Roles = "SuperAdmin,Director")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        // Traer lista de profesores para asignar
        var docentes = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente && u.Activo)
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();

        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto");
        return View();
    }

    [Authorize(Roles = "SuperAdmin,Director")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Taller taller)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        if (ModelState.IsValid)
        {
            taller.ColegioId = colegioId > 0 ? colegioId : 1;
            _context.Talleres.Add(taller);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        var docentes = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente && u.Activo)
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();

        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto");
        return View(taller);
    }
}