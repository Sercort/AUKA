using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize]
public class BusquedaController : Controller
{
    private readonly ApplicationDbContext _context;

    public BusquedaController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return View(new BusquedaResultadoViewModel { Query = string.Empty });
        }

        // Lectura segura del ColegioId del usuario en sesión
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var q = query.Trim().ToLower();

        // 1. Buscar Estudiantes (por Nombre, Apellido o RUT)
        var estudiantes = await _context.Estudiantes
            .Include(e => e.Usuario)
            .Include(e => e.Curso)
            .Where(e => (colegioId == 0 || e.Curso == null || e.Curso.ColegioId == colegioId) &&
                        e.Usuario != null &&
                        (e.Usuario.Nombre.ToLower().Contains(q) ||
                         e.Usuario.Apellido.ToLower().Contains(q) ||
                         e.Usuario.Rut.ToLower().Contains(q)))
            .ToListAsync();

        // 2. Buscar Cursos (por Nombre o Letra)
        var cursos = await _context.Cursos
            .Include(c => c.ProfesorJefe)
            .Include(c => c.Estudiantes)
            .Where(c => (colegioId == 0 || c.ColegioId == colegioId) &&
                        (c.Nombre.ToLower().Contains(q) ||
                         c.Letra.ToLower().Contains(q)))
            .ToListAsync();

        // 3. Buscar Personal / Docentes (por Nombre, Apellido o RUT)
        var personal = await _context.Usuarios
            .Where(u => (colegioId == 0 || u.ColegioId == colegioId) &&
                        u.Rol != RolUsuario.Estudiante &&
                        u.Rol != RolUsuario.Apoderado &&
                        (u.Nombre.ToLower().Contains(q) ||
                         u.Apellido.ToLower().Contains(q) ||
                         u.Rut.ToLower().Contains(q)))
            .ToListAsync();

        // 4. Buscar Asignaturas
        var asignaturas = await _context.Asignaturas
            .Include(a => a.Curso)
            .Include(a => a.Docente)
            .Where(a => (colegioId == 0 || a.Curso == null || a.Curso.ColegioId == colegioId) &&
                        a.Nombre.ToLower().Contains(q))
            .ToListAsync();

        var resultado = new BusquedaResultadoViewModel
        {
            Query = query,
            Estudiantes = estudiantes,
            Cursos = cursos,
            Personal = personal,
            Asignaturas = asignaturas
        };

        return View(resultado);
    }
}

// ViewModel para agrupar los resultados de búsqueda
public class BusquedaResultadoViewModel
{
    public string Query { get; set; } = string.Empty;
    public List<Estudiante> Estudiantes { get; set; } = new();
    public List<Curso> Cursos { get; set; } = new();
    public List<Usuario> Personal { get; set; } = new();
    public List<Asignatura> Asignaturas { get; set; } = new();

    public bool TieneResultados => Estudiantes.Any() || Cursos.Any() || Personal.Any() || Asignaturas.Any();
}