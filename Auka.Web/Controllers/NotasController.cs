using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize]
public class NotasController : Controller
{
    private readonly ApplicationDbContext _context;

    public NotasController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Visualización de Calificaciones (Filtros por Rol y Privacidad)
    [HttpGet]
    public async Task<IActionResult> Index(int? asignaturaId, int? estudianteSeleccionadoId)
    {
        // 🔒 El Inspector General no tiene acceso al libro de notas
        if (User.IsInRole("Inspector"))
        {
            TempData["Error"] = "🚫 El perfil Inspector no tiene permisos para visualizar calificaciones.";
            return RedirectToAction("Index", "Home");
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int usuarioId);

        var query = _context.Calificaciones
            .Include(c => c.Estudiante)
                .ThenInclude(e => e != null ? e.Usuario : null)
            .Include(c => c.Asignatura)
                .ThenInclude(a => a != null ? a.Curso : null)
            .AsQueryable();

        // 🔒 SI ES ESTUDIANTE: Solo ve sus propias notas
        if (User.IsInRole("Estudiante"))
        {
            query = query.Where(c => c.Estudiante != null && c.Estudiante.UsuarioId == usuarioId);
        }
        // 🔒 SI ES APODERADO: Solo ve las notas de sus hijos asociados
        else if (User.IsInRole("Apoderado"))
        {
            var apoderado = await _context.Apoderados
                .FirstOrDefaultAsync(a => a.UsuarioId == usuarioId);

            if (apoderado != null)
            {
                var estudiantesHijosIds = await _context.EstudiantesApoderados
                    .Where(ea => ea.ApoderadoId == apoderado.Id)
                    .Select(ea => ea.EstudianteId)
                    .ToListAsync();

                query = query.Where(c => estudiantesHijosIds.Contains(c.EstudianteId));
            }
            else
            {
                query = query.Where(c => false); // Si no encuentra al apoderado, lista vacía
            }
        }

        // Filtro adicional por asignatura seleccionada
        if (asignaturaId.HasValue && asignaturaId.Value > 0)
        {
            query = query.Where(c => c.AsignaturaId == asignaturaId.Value);
        }

        // Filtro adicional por estudiante seleccionado (para vista Docente/UTP)
        if (estudianteSeleccionadoId.HasValue && estudianteSeleccionadoId.Value > 0)
        {
            query = query.Where(c => c.EstudianteId == estudianteSeleccionadoId.Value);
        }

        var calificaciones = await query.OrderByDescending(c => c.Fecha).ToListAsync();

        var asignaturasList = await _context.Asignaturas
            .Include(a => a.Curso)
            .Select(a => new { a.Id, Nombre = a.Nombre + (a.Curso != null ? " - " + a.Curso.Nombre : "") })
            .ToListAsync();

        ViewBag.Asignaturas = new SelectList(asignaturasList, "Id", "Nombre", asignaturaId);
        return View(calificaciones);
    }

    // 2. SOLO EL PROFESOR / UTP puede agregar notas
    [Authorize(Roles = "Docente,UTP,SuperAdmin,Administrador")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        var estudiantesList = await _context.Estudiantes
            .Include(e => e.Usuario)
            .Where(e => e.Usuario != null)
            .Select(e => new
            {
                e.Id,
                Nombre = (e.Usuario!.Nombre + " " + e.Usuario!.Apellido).Trim()
            })
            .ToListAsync();

        var asignaturasList = await _context.Asignaturas
            .Select(a => new { a.Id, a.Nombre })
            .ToListAsync();

        ViewBag.Estudiantes = new SelectList(estudiantesList, "Id", "Nombre");
        ViewBag.Asignaturas = new SelectList(asignaturasList, "Id", "Nombre");
        return View();
    }

    [Authorize(Roles = "Docente,UTP,SuperAdmin,Administrador")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Calificacion calificacion)
    {
        if (ModelState.IsValid)
        {
            calificacion.Fecha = DateTime.UtcNow;
            _context.Calificaciones.Add(calificacion);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "✅ Calificación registrada exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        var estudiantesList = await _context.Estudiantes
            .Include(e => e.Usuario)
            .Where(e => e.Usuario != null)
            .Select(e => new { e.Id, Nombre = (e.Usuario!.Nombre + " " + e.Usuario!.Apellido).Trim() })
            .ToListAsync();

        ViewBag.Estudiantes = new SelectList(estudiantesList, "Id", "Nombre", calificacion.EstudianteId);
        ViewBag.Asignaturas = new SelectList(await _context.Asignaturas.ToListAsync(), "Id", "Nombre", calificacion.AsignaturaId);
        return View(calificacion);
    }

    // 3. SOLO PROFESOR Y UTP pueden editar/modificar notas
    [Authorize(Roles = "Docente,UTP,SuperAdmin,Administrador")]
    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var calificacion = await _context.Calificaciones
            .Include(c => c.Estudiante)
            .ThenInclude(e => e != null ? e.Usuario : null)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (calificacion == null) return NotFound();

        return View(calificacion);
    }

    [Authorize(Roles = "Docente,UTP,SuperAdmin,Administrador")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(int id, Calificacion calificacion)
    {
        if (id != calificacion.Id) return NotFound();

        if (ModelState.IsValid)
        {
            var calificacionExistente = await _context.Calificaciones.FindAsync(id);
            if (calificacionExistente == null) return NotFound();

            calificacionExistente.Nota = calificacion.Nota;
            calificacionExistente.Ponderacion = calificacion.Ponderacion;
            calificacionExistente.Descripcion = calificacion.Descripcion;

            _context.Update(calificacionExistente);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "✅ Calificación actualizada con éxito.";
            return RedirectToAction(nameof(Index));
        }

        return View(calificacion);
    }
}