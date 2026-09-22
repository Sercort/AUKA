using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
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
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();

        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto");
        return View(curso);
    }

    // 3. Ver Lista de Alumnos de un Curso Específico
    public async Task<IActionResult> Alumnos(int id)
    {
        var curso = await _context.Cursos
            .Include(c => c.ProfesorJefe)
            .Include(c => c.Estudiantes)
                .ThenInclude(e => e.Usuario)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (curso == null) return NotFound();

        var otrosCursos = await _context.Cursos
            .Where(c => c.Nombre == curso.Nombre && c.Id != curso.Id)
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

        // Trae TODOS los niveles/nombres de cursos existentes en la BD
        var nivelesEnBD = await _context.Cursos
            .Select(c => c.Nombre)
            .Distinct()
            .ToListAsync();

        // Lista completa de respaldo con todos los niveles del sistema
        var nivelesPredeterminados = new List<string>
        {
            "Prekínder", "Kínder",
            "1º Básico", "2º Básico", "3º Básico", "4º Básico", "5º Básico", "6º Básico", "7º Básico", "8º Básico",
            "1º Medio", "2º Medio", "3º Medio HC", "4º Medio HC", "Cuarto Medio",
            "3º Medio TP - Administración", "3º Medio TP - Electricidad", "3º Medio TP - Programación", "3º Medio TP - Mecánica Industrial",
            "4º Medio TP - Administración", "4º Medio TP - Electricidad", "4º Medio TP - Programación", "4º Medio TP - Mecánica Industrial"
        };

        // Une los niveles existentes y los estándar sin duplicados
        var todosLosNiveles = nivelesEnBD.Union(nivelesPredeterminados).OrderBy(n => n).ToList();

        ViewBag.Niveles = new SelectList(todosLosNiveles);
        return View();
    }

    // 5. Procesar Matrícula (POST) - Vinculación de Estudiante y Apoderado por RUT
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

            // A) Buscar un curso existente que coincida con el nivel elegido
            var cursoSeleccionado = await _context.Cursos
                .Include(c => c.Estudiantes)
                .Where(c => c.Nombre.ToLower() == nivel.ToLower())
                .OrderBy(c => c.Estudiantes.Count)
                .FirstOrDefaultAsync();

            // Si el curso no existe en la BD aún, se crea automáticamente
            if (cursoSeleccionado == null)
            {
                cursoSeleccionado = new Curso
                {
                    Nombre = nivel,
                    Letra = "A",
                    CapacidadMaxima = 40,
                    ColegioId = colegioId
                };
                _context.Cursos.Add(cursoSeleccionado);
                await _context.SaveChangesAsync();
            }

            // B) BUSCAR O CREAR APODERADO (Vinculación automática de familia por RUT)
            var apoderado = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Rut == rutApoderado);

            if (apoderado == null)
            {
                apoderado = new Usuario
                {
                    Rut = string.IsNullOrWhiteSpace(rutApoderado) ? "99999999-9" : rutApoderado,
                    Nombre = nombreApoderado,
                    Apellido = apellidoApoderado,
                    Email = emailApoderado,
                    PasswordHash = "Clave123*",
                    Rol = RolUsuario.Apoderado,
                    ColegioId = colegioId,
                    Activo = true,
                    FechaCreacion = DateTime.Now
                };
                _context.Usuarios.Add(apoderado);
                await _context.SaveChangesAsync();
            }

            // C) Crear Usuario para el Estudiante
            var usuarioEstudiante = new Usuario
            {
                Rut = string.IsNullOrWhiteSpace(rut) ? "12345678-9" : rut,
                Nombre = nombre,
                Apellido = apellido,
                Email = email,
                PasswordHash = "Clave123*",
                Rol = RolUsuario.Estudiante,
                ColegioId = colegioId,
                Activo = true,
                FechaCreacion = DateTime.Now
            };

            _context.Usuarios.Add(usuarioEstudiante);
            await _context.SaveChangesAsync();

            // D) Crear Registro de Estudiante vinculado al Curso y a la Familia
            var estudiante = new Estudiante
            {
                UsuarioId = usuarioEstudiante.Id,
                CursoId = cursoSeleccionado.Id,
                TipoMatricula = tipoMatricula,
                FechaMatricula = DateTime.Now
            };

            _context.Estudiantes.Add(estudiante);
            await _context.SaveChangesAsync();

            // E) Notificación de Éxito
            string tipoTexto = tipoMatricula switch
            {
                TipoMatricula.Nuevo => "Alumno Nuevo",
                TipoMatricula.Renovacion => "Renovación",
                _ => "Traslado"
            };

            TempData["SuccessMessage"] = $"✅ ¡Estudiante {nombre} {apellido} matriculado con éxito! Asignado a: {cursoSeleccionado.Nombre} \"{cursoSeleccionado.Letra}\" [{tipoTexto}]. Apoderado vinculado: {apoderado.Nombre} {apoderado.Apellido}. Contraseña inicial: Clave123*";

            return RedirectToAction("Alumnos", new { id = cursoSeleccionado.Id });
        }
        catch (Exception ex)
        {
            TempData["InfoMessage"] = $"Error al guardar en la base de datos: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    // 6. Cambiar Manualmente de Curso a un Estudiante (Por Excepción)
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

            TempData["SuccessMessage"] = "El estudiante fue cambiado de curso por excepción correctamente.";
        }

        return RedirectToAction("Alumnos", new { id = nuevoCursoId });
    }
}