using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

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
    public async Task<IActionResult> Index(int? estudianteId, int? cursoId)
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

        // D) Sin alumno elegido: primero se elige el curso; luego, los alumnos/as de ese curso
        var anio = DateTime.Now.Year;
        var desdeAnio = new DateTime(anio, 1, 1, 0, 0, 0, DateTimeKind.Utc);   // se cuentan las del año escolar
        if (cursoId == null)
        {
            _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colAl);
            ViewBag.Estados = await Auka.Web.Services.Alertas.EstadoCursosAsync(_context, colAl);
            var cursos = await _context.Cursos.AsNoTracking()
                .Where(c => c.Nombre != "Pacientes" && c.AnioAcademico == anio)
                .Select(c => new CursoAnotVm(c.Id, c.Nombre, c.Letra, c.Estudiantes.Count,
                    _context.Anotaciones.Count(a => a.Estudiante != null && a.Estudiante.CursoId == c.Id && a.Tipo == TipoAnotacion.Positiva && a.Fecha >= desdeAnio),
                    _context.Anotaciones.Count(a => a.Estudiante != null && a.Estudiante.CursoId == c.Id && a.Tipo == TipoAnotacion.Negativa && a.Fecha >= desdeAnio)))
                .ToListAsync();
            return View("SeleccionarCurso", cursos.OrderBy(c => c.Nombre).ThenBy(c => c.Letra).ToList());
        }

        var curso = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cursoId);
        if (curso == null) return RedirectToAction(nameof(Index));
        var alumnos = await _context.Estudiantes.AsNoTracking()
            .Where(e => e.CursoId == cursoId && e.Usuario != null)
            .Select(e => new AlumnoAnotVm(e.Id, e.Usuario!.Nombre, e.Usuario.Apellido, e.Usuario.Rut,
                _context.Anotaciones.Count(a => a.EstudianteId == e.Id && a.Tipo == TipoAnotacion.Positiva && a.Fecha >= desdeAnio),
                _context.Anotaciones.Count(a => a.EstudianteId == e.Id && a.Tipo == TipoAnotacion.Negativa && a.Fecha >= desdeAnio)))
            .ToListAsync();
        ViewBag.Curso = curso;
        return View("SeleccionarEstudiante", alumnos.OrderBy(a => a.Apellido).ThenBy(a => a.Nombre).ToList());
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
            Fecha = DateTime.UtcNow
        };

        _context.Anotaciones.Add(nuevaAnotacion);
        await _context.SaveChangesAsync();

        // Alertas: curso en alerta o alumno/a con 3 negativas → aviso en la campana (una vez por período)
        if (tipo == TipoAnotacion.Negativa)
        {
            try { await Auka.Web.Services.Alertas.RevisarTrasAnotacionAsync(_context, estudianteId); }
            catch { /* el aviso nunca debe impedir registrar la anotación */ }
        }

        TempData["SuccessMessage"] = "Anotación registrada con éxito.";
        return RedirectToAction(nameof(Index), new { estudianteId });
    }
}

public record CursoAnotVm(int Id, string Nombre, string Letra, int Alumnos, int Positivas30, int Negativas30);
public record AlumnoAnotVm(int Id, string Nombre, string Apellido, string Rut, int Positivas, int Negativas);
