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

    // 1. Vista general de Talleres / Clubes
    //    - Administración (SuperAdmin, Director, UTP, Administrador): ve todos los talleres del colegio
    //    - Docente: ve solo los talleres donde es el profesor a cargo
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int userId);

        var rol = User.FindFirst(ClaimTypes.Role)?.Value;
        var esDocente = string.Equals(rol, RolUsuario.Docente.ToString(), StringComparison.OrdinalIgnoreCase);

        IQueryable<Taller> query = _context.Talleres
            .Include(t => t.Estudiantes)
            .Include(t => t.Docente)
            .Where(t => t.ColegioId == colegioId || colegioId == 0);

        if (esDocente)
        {
            query = query.Where(t => t.DocenteId == userId);
        }

        var talleres = await query.OrderBy(t => t.Nombre).ToListAsync();

        ViewBag.Rol = rol;
        ViewBag.UserId = userId;

        return View(talleres);
    }

    // 2. Crear Taller (GET - carga de docentes)
    [Authorize(Roles = "SuperAdmin,Director,UTP,Administrador")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        await CargarDocentesAsync();
        return View();
    }

    // 3. Crear Taller (POST - registro con docente y asignación de ColegioId)
    [Authorize(Roles = "SuperAdmin,Director,UTP,Administrador")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Taller taller)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        // Estas propiedades no vienen del formulario como entidades completas
        ModelState.Remove(nameof(Taller.Docente));
        ModelState.Remove(nameof(Taller.Estudiantes));

        Usuario? docente = null;

        if (taller.DocenteId == null)
        {
            ModelState.AddModelError(nameof(Taller.DocenteId), "Debes seleccionar un profesor.");
        }
        else
        {
            docente = await _context.Usuarios.FirstOrDefaultAsync(u =>
                u.Id == taller.DocenteId
                && u.Rol == RolUsuario.Docente
                && u.Activo
                && (u.ColegioId == colegioId || colegioId == 0));

            if (docente == null)
            {
                ModelState.AddModelError(nameof(Taller.DocenteId), "El profesor seleccionado no es válido.");
            }
        }

        if (ModelState.IsValid && docente != null)
        {
            taller.ColegioId = colegioId > 0 ? colegioId : 1;
            taller.DocenteCargo = $"{docente.Nombre} {docente.Apellido}";

            _context.Talleres.Add(taller);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "✅ Taller / Club registrado exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        await CargarDocentesAsync(taller.DocenteId);
        return View(taller);
    }

    // Método auxiliar: carga el combo de docentes del colegio
    private async Task CargarDocentesAsync(int? docenteSeleccionado = null)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var docentes = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente
                        && u.Activo
                        && (u.ColegioId == colegioId || colegioId == 0))
            .OrderBy(u => u.Apellido)
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();

        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto", docenteSeleccionado);
    }
}