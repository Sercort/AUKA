using System.Security.Claims;
using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

[Authorize]
public class NotasController : Controller
{
    private readonly ApplicationDbContext _context;

    public NotasController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Visualización de Calificaciones
    public async Task<IActionResult> Index(int? asignaturaId, int? estudianteSeleccionadoId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int usuarioId);

        var query = _context.Calificaciones
            .Include(c => c.Estudiante)
                .ThenInclude(e => e.Usuario)
            .Include(c => c.Asignatura)
                .ThenInclude(a => a.Curso)
            .AsQueryable();

        // 🔒 SI ES ESTUDIANTE: Solo ve sus propias notas
        if (User.IsInRole("Estudiante"))
        {
            query = query.Where(c => c.Estudiante.UsuarioId == usuarioId);
        }

        // Filtro adicional por asignatura
        if (asignaturaId.HasValue)
        {
            query = query.Where(c => c.AsignaturaId == asignaturaId.Value);
        }

        var calificaciones = await query.OrderByDescending(c => c.Fecha).ToListAsync();

        ViewBag.Asignaturas = new SelectList(await _context.Asignaturas.Include(a => a.Curso).ToListAsync(), "Id", "Nombre");
        return View(calificaciones);
    }

    // 2. SOLO EL PROFESOR puede agregar notas
    [Authorize(Roles = "Docente")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        ViewBag.Estudiantes = new SelectList(await _context.Estudiantes.Include(e => e.Usuario).Select(e => new { e.Id, Nombre = e.Usuario.Nombre + " " + e.Usuario.Apellido }).ToListAsync(), "Id", "Nombre");
        ViewBag.Asignaturas = new SelectList(await _context.Asignaturas.ToListAsync(), "Id", "Nombre");
        return View();
    }

    [Authorize(Roles = "Docente")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Calificacion calificacion)
    {
        if (ModelState.IsValid)
        {
            calificacion.Fecha = DateTime.Now;
            _context.Calificaciones.Add(calificacion);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(calificacion);
    }

    // 3. SOLO PROFESOR Y UTP pueden editar/modificar notas
    [Authorize(Roles = "Docente,UTP")]
    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var calificacion = await _context.Calificaciones.FindAsync(id);
        if (calificacion == null) return NotFound();

        return View(calificacion);
    }

    [Authorize(Roles = "Docente,UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(int id, Calificacion calificacion)
    {
        if (id != calificacion.Id) return NotFound();

        if (ModelState.IsValid)
        {
            _context.Update(calificacion);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(calificacion);
    }
}