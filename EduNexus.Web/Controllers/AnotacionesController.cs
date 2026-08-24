using System.Security.Claims;
using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

[Authorize]
public class AnotacionesController : Controller
{
    private readonly ApplicationDbContext _context;

    public AnotacionesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Vista de Anotaciones con Privacidad Garantizada
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Inspector,Estudiante,Apoderado")]
    public async Task<IActionResult> Index(int? estudianteId)
    {
        var rol = User.FindFirst(ClaimTypes.Role)?.Value;
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out int usuarioLogueadoId);

        // A) Si es ESTUDIANTE: Forzar privacidad estricta (Solo ve sus propias anotaciones)
        if (User.IsInRole("Estudiante") || rol == "Estudiante")
        {
            var estudiante = await _context.Estudiantes
                .FirstOrDefaultAsync(e => e.UsuarioId == usuarioLogueadoId || e.Id == usuarioLogueadoId);

            int targetEstudianteId = estudiante != null ? estudiante.Id : usuarioLogueadoId;

            var anotacionesAlumno = await _context.Anotaciones
                .AsNoTracking()
                .Where(a => a.EstudianteId == targetEstudianteId)
                .OrderByDescending(a => a.Fecha)
                .ToListAsync();

            return View("MisAnotaciones", anotacionesAlumno);
        }

        // B) Si es APODERADO: Solo ve las de sus pupilos
        if (User.IsInRole("Apoderado") || rol == "Apoderado")
        {
            var anotacionesPupilo = await _context.Anotaciones
                .AsNoTracking()
                .Where(a => a.EstudianteId == estudianteId)
                .OrderByDescending(a => a.Fecha)
                .ToListAsync();

            return View("MisAnotaciones", anotacionesPupilo);
        }

        // C) Si es DOCENTE / INSPECTOR / ADMIN / DIRECTOR y ya seleccionó un estudiante
        if (estudianteId.HasValue)
        {
            var anotacionesEstudiante = await _context.Anotaciones
                .AsNoTracking()
                .Where(a => a.EstudianteId == estudianteId.Value)
                .OrderByDescending(a => a.Fecha)
                .ToListAsync();

            var estudiante = await _context.Estudiantes
                .Include(e => e.Usuario)
                .Include(e => e.Curso)
                .FirstOrDefaultAsync(e => e.Id == estudianteId.Value);

            ViewBag.Estudiante = estudiante;
            ViewBag.EstudianteId = estudianteId.Value;

            return View("GestionAnotaciones", anotacionesEstudiante);
        }

        // D) Si NO ha seleccionado estudiante, listamos ordenando por la entidad Usuario
        var estudiantes = await _context.Estudiantes
            .AsNoTracking()
            .Include(e => e.Usuario)
            .Include(e => e.Curso)
            .OrderBy(e => e.Usuario != null ? e.Usuario.Apellido : "")
            .ThenBy(e => e.Usuario != null ? e.Usuario.Nombre : "")
            .ToListAsync();

        return View("SeleccionarEstudiante", estudiantes);
    }

    // 2. Crear Anotación
    [Authorize(Roles = "Docente,Inspector,SuperAdmin,Director")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(int estudianteId, TipoAnotacion tipo, string detalle)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out int profesorId);

        var existeEstudiante = await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId);
        if (!existeEstudiante)
        {
            TempData["ErrorMessage"] = "El estudiante seleccionado no existe en el sistema.";
            return RedirectToAction(nameof(Index));
        }

        var nuevaAnotacion = new Anotacion
        {
            EstudianteId = estudianteId,
            DocenteId = profesorId,
            Tipo = tipo,
            Detalle = detalle,
            Fecha = DateTime.Now
        };

        _context.Anotaciones.Add(nuevaAnotacion);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Anotación registrada con éxito.";
        return RedirectToAction(nameof(Index), new { estudianteId });
    }
}