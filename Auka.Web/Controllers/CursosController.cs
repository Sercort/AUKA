using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize]
public class CursosController : Controller
{
    private readonly ApplicationDbContext _context;

    public CursosController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Vista General de Cursos
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var cursos = await _context.Cursos
            .Include(c => c.ProfesorJefe)
            .Include(c => c.Estudiantes)
            .Where(c => c.ColegioId == colegioId || colegioId == 0)
            .OrderBy(c => c.Nombre)
            .ThenBy(c => c.Letra)
            .ToListAsync();

        return View(cursos);
    }

    // 2. Crear Nuevo Curso / Configurar Capacidad (SuperAdmin y Director)
    [Authorize(Roles = "SuperAdmin,Director")]
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var docentes = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente && u.Activo)
            .Where(u => u.ColegioId == colegioId || colegioId == 0)
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();

        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto");
        return View();
    }

    [Authorize(Roles = "SuperAdmin,Director")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Curso curso)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        if (ModelState.IsValid)
        {
            curso.ColegioId = colegioId > 0 ? colegioId : 1;
            _context.Cursos.Add(curso);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Curso {curso.Nombre} \"{curso.Letra}\" creado exitosamente con capacidad para {curso.CapacidadMaxima} alumnos.";
            return RedirectToAction(nameof(Index));
        }

        var docentes = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente && u.Activo)
            .Where(u => u.ColegioId == colegioId || colegioId == 0)
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();

        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto");
        return View(curso);
    }

    // 3. Ver Lista de Alumnos de un Curso Específico
    [HttpGet]
    public async Task<IActionResult> Alumnos(int id)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var curso = await _context.Cursos
            .Include(c => c.ProfesorJefe)
            .Include(c => c.Estudiantes)
                .ThenInclude(e => e.Usuario)
            .FirstOrDefaultAsync(c => c.Id == id && (c.ColegioId == colegioId || colegioId == 0));

        if (curso == null) return NotFound();

        var otrosCursos = await _context.Cursos
            .Where(c => c.ColegioId == colegioId && c.Id != curso.Id)
            .ToListAsync();

        ViewBag.OtrosCursos = otrosCursos;
        return View(curso);
    }

    // 4. Formulario de Matrícula (GET)
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpGet]
    public async Task<IActionResult> Matricular()
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var nivelesEnBD = await _context.Cursos
            .Where(c => c.ColegioId == colegioId || colegioId == 0)
            .Select(c => c.Nombre)
            .Distinct()
            .ToListAsync();

        var nivelesPredeterminados = new List<string>
        {
            "Prekínder", "Kínder",
            "1º Básico", "2º Básico", "3º Básico", "4º Básico", "5º Básico", "6º Básico", "7º Básico", "8º Básico",
            "1º Medio", "2º Medio", "3º Medio HC", "4º Medio HC", "Cuarto Medio",
            "3º Medio TP - Administración", "3º Medio TP - Electricidad", "3º Medio TP - Programación", "3º Medio TP - Mecánica Industrial",
            "4º Medio TP - Administración", "4º Medio TP - Electricidad", "4º Medio TP - Programación", "4º Medio TP - Mecánica Industrial"
        };

        var todosLosNiveles = nivelesEnBD.Union(nivelesPredeterminados).OrderBy(n => n).ToList();

        ViewBag.Niveles = new SelectList(todosLosNiveles);
        return View();
    }

    // 5. Procesar Matrícula (POST) - Solución Robusta de Guardado
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Matricular(
        string rut,
        string nombre,
        string apellido,
        string email,
        string nivel,
        TipoMatricula tipoMatricula,
        int? cursoIdAnterior,
        string rutApoderado,
        string nombreApoderado,
        string apellidoApoderado,
        string emailApoderado,
        string telefonoContacto)
    {
        try
        {
            var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
            int.TryParse(colegioIdClaim, out int colegioId);
            colegioId = colegioId > 0 ? colegioId : 1;

            var hasher = new PasswordHasher<string>();

            // A) Buscar o crear el curso de forma privada por ColegioId
            string nivelLimpio = string.IsNullOrWhiteSpace(nivel) ? "General" : nivel.Trim();
            var cursoSeleccionado = await _context.Cursos
                .Include(c => c.Estudiantes)
                .Where(c => c.ColegioId == colegioId && c.Nombre.ToLower() == nivelLimpio.ToLower())
                .OrderBy(c => c.Estudiantes.Count)
                .FirstOrDefaultAsync();

            if (cursoSeleccionado == null)
            {
                cursoSeleccionado = new Curso
                {
                    Nombre = nivelLimpio,
                    Letra = "A",
                    CapacidadMaxima = 40,
                    ColegioId = colegioId
                };
                _context.Cursos.Add(cursoSeleccionado);
                await _context.SaveChangesAsync();
            }

            // B) BUSCAR O CREAR APODERADO
            string rutApoLimpio = string.IsNullOrWhiteSpace(rutApoderado) ? "99999999-9" : rutApoderado.Trim();
            string emailApoLimpio = string.IsNullOrWhiteSpace(emailApoderado) ? $"apo.{rutApoLimpio.Replace("-", "")}@auka.cl" : emailApoderado.Trim().ToLower();

            var apoderado = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Rut == rutApoLimpio || u.Email.ToLower() == emailApoLimpio);

            if (apoderado == null)
            {
                apoderado = new Usuario
                {
                    Rut = rutApoLimpio,
                    Nombre = string.IsNullOrWhiteSpace(nombreApoderado) ? "Apoderado" : nombreApoderado.Trim(),
                    Apellido = string.IsNullOrWhiteSpace(apellidoApoderado) ? "Registrado" : apellidoApoderado.Trim(),
                    Email = emailApoLimpio,
                    Telefono = telefonoContacto?.Trim(),
                    Rol = RolUsuario.Apoderado,
                    ColegioId = colegioId,
                    Activo = true,
                    DebeCambiarPassword = true,
                    FechaCreacion = DateTime.UtcNow
                };
                apoderado.PasswordHash = hasher.HashPassword(emailApoLimpio, "123456");

                _context.Usuarios.Add(apoderado);
                await _context.SaveChangesAsync();
            }

            // C) BUSCAR O CREAR USUARIO ESTUDIANTE
            string rutEstLimpio = string.IsNullOrWhiteSpace(rut) ? "12345678-9" : rut.Trim();
            string emailEstLimpio = string.IsNullOrWhiteSpace(email) ? $"alumno.{rutEstLimpio.Replace("-", "")}@auka.cl" : email.Trim().ToLower();

            var usuarioEstudiante = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Rut == rutEstLimpio || u.Email.ToLower() == emailEstLimpio);

            if (usuarioEstudiante == null)
            {
                usuarioEstudiante = new Usuario
                {
                    Rut = rutEstLimpio,
                    Nombre = nombre.Trim(),
                    Apellido = apellido.Trim(),
                    Email = emailEstLimpio,
                    Rol = RolUsuario.Estudiante,
                    ColegioId = colegioId,
                    Activo = true,
                    DebeCambiarPassword = true,
                    FechaCreacion = DateTime.UtcNow
                };
                usuarioEstudiante.PasswordHash = hasher.HashPassword(emailEstLimpio, "123456");

                _context.Usuarios.Add(usuarioEstudiante);
                await _context.SaveChangesAsync();
            }

            // D) BUSCAR O CREAR REGISTRO DE ESTUDIANTE
            var estudiante = await _context.Estudiantes
                .FirstOrDefaultAsync(e => e.UsuarioId == usuarioEstudiante.Id);

            if (estudiante == null)
            {
                estudiante = new Estudiante
                {
                    UsuarioId = usuarioEstudiante.Id,
                    CursoId = cursoSeleccionado.Id,
                    TipoMatricula = tipoMatricula,
                    FechaMatricula = DateTime.UtcNow
                };
                _context.Estudiantes.Add(estudiante);
            }
            else
            {
                estudiante.CursoId = cursoSeleccionado.Id;
                estudiante.TipoMatricula = tipoMatricula;
                _context.Estudiantes.Update(estudiante);
            }

            await _context.SaveChangesAsync();

            string tipoTexto = tipoMatricula switch
            {
                TipoMatricula.Nuevo => "Alumno Nuevo",
                TipoMatricula.Renovacion => "Renovación",
                _ => "Traslado"
            };

            TempData["SuccessMessage"] = $"✅ ¡Estudiante {nombre} {apellido} matriculado con éxito! Asignado a: {cursoSeleccionado.Nombre} \"{cursoSeleccionado.Letra}\" [{tipoTexto}]. Clave inicial: 123456";

            return RedirectToAction("Alumnos", new { id = cursoSeleccionado.Id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al guardar en la base de datos: {ex.InnerException?.Message ?? ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    // 6. Cambiar Manualmente de Curso a un Estudiante
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarCursoAlumno(int estudianteId, int nuevoCursoId, string? motivo)
    {
        var estudiante = await _context.Estudiantes.FindAsync(estudianteId);
        if (estudiante != null)
        {
            estudiante.CursoId = nuevoCursoId;
            estudiante.FueCambiadoPorExcepcion = true;
            estudiante.MotivoCambioExcepcion = string.IsNullOrWhiteSpace(motivo) ? "Cambio por excepción directiva/UTP" : motivo;

            _context.Estudiantes.Update(estudiante);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "El estudiante fue cambiado de curso correctamente.";
        }

        return RedirectToAction("Alumnos", new { id = nuevoCursoId });
    }
}