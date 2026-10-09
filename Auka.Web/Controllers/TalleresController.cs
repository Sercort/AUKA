using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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

    // 1. VISTA GENERAL DE TALLERES / CLUBES (ACLE)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int userId);

        var rol = User.FindFirst(ClaimTypes.Role)?.Value;
        var esDocente = string.Equals(rol, RolUsuario.Docente.ToString(), StringComparison.OrdinalIgnoreCase);

        // 🛡️ .IgnoreQueryFilters() evita el error 'PostgresException 42703' por columnas inexistentes
        var query = _context.Talleres
            .IgnoreQueryFilters()
            .Include(t => t.Estudiantes)
            .Include(t => t.Docente)
            .Where(t => t.ColegioId == colegioId || colegioId == 0)
            .AsQueryable();

        // Si el usuario con sesión activa es Docente, filtra sus talleres asignados
        if (esDocente)
        {
            query = query.Where(t => t.DocenteId == userId);
        }

        var talleres = await query.OrderBy(t => t.Nombre).ToListAsync();

        ViewBag.Rol = rol;
        ViewBag.UserId = userId;

        return View(talleres);
    }

    // 2. CREAR TALLER (GET - Carga Combo de Docentes)
    [Authorize(Roles = "SuperAdmin,Director,UTP,Administrador")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        await CargarDocentesAsync();
        return View();
    }

    // 3. CREAR TALLER (POST - Registro con Validación Estricta)
    [Authorize(Roles = "SuperAdmin,Director,UTP,Administrador")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Taller taller)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        // Remover objetos navegacionales de la validación del modelo Razor
        ModelState.Remove(nameof(Taller.Docente));
        ModelState.Remove(nameof(Taller.Estudiantes));

        Usuario? docente = null;

        if (taller.DocenteId == null || taller.DocenteId == 0)
        {
            ModelState.AddModelError(nameof(Taller.DocenteId), "Debes seleccionar un profesor a cargo.");
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
                ModelState.AddModelError(nameof(Taller.DocenteId), "El profesor seleccionado no es válido o está inactivo.");
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

    // Método Auxiliar: Carga la lista desplegable de Docentes del Establecimiento
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