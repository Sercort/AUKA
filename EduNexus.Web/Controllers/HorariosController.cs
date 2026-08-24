using System.Security.Claims;
using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

[Authorize]
public class HorariosController : Controller
{
    private readonly ApplicationDbContext _context;

    public HorariosController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Matriz General de Horarios por Curso (Views/Horarios/Index.cshtml)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out int usuarioId);

        if (User.IsInRole("Estudiante"))
        {
            var estudianteActual = await _context.Estudiantes
                .Include(e => e.Curso)
                .FirstOrDefaultAsync(e => e.UsuarioId == usuarioId);

            if (estudianteActual != null && estudianteActual.Curso != null)
            {
                var listaCursos = new List<Curso> { estudianteActual.Curso };
                return View(listaCursos);
            }
        }

        var todosLosCursos = await _context.Cursos.ToListAsync();
        return View(todosLosCursos);
    }

    // 2. Detalle del Horario del Curso (Views/Horarios/Curso.cshtml)
    [HttpGet]
    [Route("Horarios/Curso/{id}")]
    [Route("Horarios/VerHorarioCurso/{id}")]
    public async Task<IActionResult> VerHorarioCurso(int id)
    {
        var curso = await _context.Cursos.FindAsync(id);
        if (curso == null)
        {
            return NotFound();
        }

        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        // Cargar asignaturas
        ViewBag.Asignaturas = await _context.Asignaturas
            .Where(a => a.CursoId == id || id == 0)
            .OrderBy(a => a.Nombre)
            .ToListAsync();

        if (ViewBag.Asignaturas == null || ((List<Asignatura>)ViewBag.Asignaturas).Count == 0)
        {
            ViewBag.Asignaturas = await _context.Asignaturas.ToListAsync();
        }

        // Cargar profesores
        ViewBag.Profesores = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente || u.Rol == RolUsuario.UTP || u.Rol == RolUsuario.Director)
            .OrderBy(u => u.Apellido)
            .ToListAsync();

        // Obtener bloques de horario
        var horariosDelCurso = await _context.Set<HorarioClase>()
            .Include(h => h.Asignatura)
            .Where(h => h.CursoId == id || (h.Asignatura != null && h.Asignatura.CursoId == id))
            .ToListAsync();

        ViewBag.Curso = curso;
        ViewBag.CursoId = id;

        return View("Curso", horariosDelCurso);
    }

    // 3. Horario Personal del Docente / Inspector
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente")]
    public async Task<IActionResult> HorarioPersonal()
    {
        var personal = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente)
            .OrderBy(u => u.Nombre)
            .ToListAsync();

        return View(personal);
    }
}