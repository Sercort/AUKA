using System.Security.Claims;
using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

[Authorize]
public class EvaluacionesController : Controller
{
    private readonly ApplicationDbContext _context;

    public EvaluacionesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Consulta general del Calendario por Cursos
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Estudiante,Apoderado")]
    public async Task<IActionResult> Index(int? cursoId)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int usuarioId);

        // 🔒 Si es Estudiante y no seleccionó curso, le asignamos automáticamente el de su matrícula
        if (User.IsInRole("Estudiante") && !cursoId.HasValue)
        {
            var estudiante = await _context.Estudiantes
                .FirstOrDefaultAsync(e => e.UsuarioId == usuarioId);

            if (estudiante != null)
            {
                cursoId = estudiante.CursoId;
            }
        }

        if (!cursoId.HasValue)
        {
            var cursos = await _context.Cursos
                .AsNoTracking()
                .Include(c => c.ProfesorJefe)
                .Where(c => c.ColegioId == colegioId || colegioId == 0)
                .OrderBy(c => c.Nombre)
                .ThenBy(c => c.Letra)
                .ToListAsync();

            return View("SeleccionarCurso", cursos);
        }

        var curso = await _context.Cursos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cursoId.Value);

        if (curso == null) return NotFound();

        var evaluaciones = await _context.Evaluaciones
            .AsNoTracking()
            .Include(e => e.Asignatura)
            .Where(e => e.Asignatura != null && e.Asignatura.CursoId == cursoId.Value)
            .OrderBy(e => e.Fecha)
            .ToListAsync();

        ViewBag.Curso = curso;

        return View(evaluaciones);
    }

    // 2. Formulario para Crear Evaluación (RESTRINGIDO: Solo Docente)
    [Authorize(Roles = "Docente")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        var asignaturas = await _context.Asignaturas
            .AsNoTracking()
            .Include(a => a.Curso)
            .Select(a => new
            {
                a.Id,
                NombreCompleto = $"{a.Nombre} - {a.Curso.Nombre} {a.Curso.Letra}"
            })
            .ToListAsync();

        ViewBag.Asignaturas = new SelectList(asignaturas, "Id", "NombreCompleto");
        return View();
    }

    // 3. Guardar Evaluación (RESTRINGIDO: Solo Docente)
    [Authorize(Roles = "Docente")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Evaluacion evaluacion)
    {
        if (ModelState.IsValid)
        {
            _context.Evaluaciones.Add(evaluacion);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Evaluación programada con éxito.";

            var asignatura = await _context.Asignaturas.FindAsync(evaluacion.AsignaturaId);
            if (asignatura != null)
            {
                return RedirectToAction(nameof(Index), new { cursoId = asignatura.CursoId });
            }

            return RedirectToAction(nameof(Index));
        }

        var asignaturas = await _context.Asignaturas
            .AsNoTracking()
            .Include(a => a.Curso)
            .Select(a => new
            {
                a.Id,
                NombreCompleto = $"{a.Nombre} - {a.Curso.Nombre} {a.Curso.Letra}"
            })
            .ToListAsync();

        ViewBag.Asignaturas = new SelectList(asignaturas, "Id", "NombreCompleto");
        return View(evaluacion);
    }
}