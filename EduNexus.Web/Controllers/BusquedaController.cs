using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

[Authorize]
public class BusquedaController : Controller
{
    private readonly ApplicationDbContext _context;

    public BusquedaController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return View(new BusquedaResultadoViewModel { Query = string.Empty });
        }

        var q = query.Trim().ToLower();

        // 1. Buscar Estudiantes (por Nombre, Apellido o RUT)
        var estudiantes = await _context.Estudiantes
            .Include(e => e.Usuario)
            .Include(e => e.Curso)
            .Where(e => e.Usuario.Nombre.ToLower().Contains(q) ||
                        e.Usuario.Apellido.ToLower().Contains(q) ||
                        e.Usuario.Rut.ToLower().Contains(q))
            .ToListAsync();

        // 2. Buscar Cursos (por Nombre o Letra)
        var cursos = await _context.Cursos
            .Include(c => c.ProfesorJefe)
            .Include(c => c.Estudiantes)
            .Where(c => c.Nombre.ToLower().Contains(q) ||
                        c.Letra.ToLower().Contains(q))
            .ToListAsync();

        // 3. Buscar Personal / Docentes (por Nombre, Apellido o RUT)
        var personal = await _context.Usuarios
            .Where(u => u.Rol != Application.Entities.RolUsuario.Estudiante &&
                        u.Rol != Application.Entities.RolUsuario.Apoderado &&
                        (u.Nombre.ToLower().Contains(q) || u.Apellido.ToLower().Contains(q) || u.Rut.ToLower().Contains(q)))
            .ToListAsync();

        // 4. Buscar Asignaturas
        var asignaturas = await _context.Asignaturas
            .Include(a => a.Curso)
            .Include(a => a.Docente)
            .Where(a => a.Nombre.ToLower().Contains(q))
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

// ViewModel para agrupar los resultados
public class BusquedaResultadoViewModel
{
    public string Query { get; set; } = string.Empty;
    public List<EduNexus.Application.Entities.Estudiante> Estudiantes { get; set; } = new();
    public List<EduNexus.Application.Entities.Curso> Cursos { get; set; } = new();
    public List<EduNexus.Application.Entities.Usuario> Personal { get; set; } = new();
    public List<EduNexus.Application.Entities.Asignatura> Asignaturas { get; set; } = new();

    public bool TieneResultados => Estudiantes.Any() || Cursos.Any() || Personal.Any() || Asignaturas.Any();
}