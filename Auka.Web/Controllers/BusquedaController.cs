using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Buscador de arriba (para todos los roles). Busca sin importar mayúsculas, tildes ni errores de tipeo,
/// con nombre y apellido juntos o una sola palabra (ver Services/Buscador.cs).
/// Mientras se escribe, /Busqueda/Sugerir muestra las opciones; al apretar Buscar se ven todos los resultados.
/// Alumnos/as y apoderados/as solo encuentran al personal del colegio (no ven datos de otros niños).
/// </summary>
[Authorize]
public class BusquedaController : Controller
{
    private readonly ApplicationDbContext _context;
    public BusquedaController(ApplicationDbContext context) => _context = context;

    private static readonly string[] RolesFicha = { "SuperAdmin", "Director", "UTP", "Docente", "Inspector", "Orientadora" };
    private bool VeFichas() => RolesFicha.Any(User.IsInRole);
    private bool VeAlumnos() => !(User.IsInRole("Estudiante") || User.IsInRole("Apoderado"));
    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }

    private async Task<BusquedaResultadoViewModel> BuscarAsync(string query, int maxAlumnos, int maxPersonal, int maxCursos)
    {
        var colegioId = ColegioId();
        var palabras = Buscador.Palabras(query);
        var rut = Buscador.EsRut(query) ? Buscador.RutPlano(query) : null;
        var res = new BusquedaResultadoViewModel { Query = query };
        if (palabras.Length == 0) return res;
        double P(string nombre, string? rutPersona) =>
            rut != null ? (Buscador.RutPlano(rutPersona).Contains(rut) ? 10 : 0) : Buscador.Puntaje(palabras, nombre);

        // 1. Alumnos/as (en memoria: así perdona errores de tipeo)
        if (VeAlumnos())
        {
            var anio = DateTime.Now.Year;
            var estudiantes = await _context.Estudiantes.AsNoTracking().Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.Usuario != null && e.Usuario.Activo && (colegioId == 0 || e.ColegioId == colegioId) && (e.Curso == null || e.Curso.Nombre != "Pacientes"))
                .ToListAsync();
            res.Estudiantes = estudiantes.Select(e => (e, s: P(e.Usuario!.Nombre + " " + e.Usuario.Apellido, e.Usuario.Rut)))
                .Where(x => x.s > 0).OrderByDescending(x => x.s).ThenBy(x => x.e.Curso?.AnioAcademico == anio ? 0 : 1).ThenBy(x => x.e.Usuario!.Apellido)
                .Take(maxAlumnos).Select(x => x.e).ToList();

            // Cursos ("1 basico a", "4º medio", "3b")
            var cursos = await _context.Cursos.AsNoTracking().Include(c => c.ProfesorJefe).Include(c => c.Estudiantes)
                .Where(c => (colegioId == 0 || c.ColegioId == colegioId) && c.AnioAcademico == anio && c.Nombre != "Pacientes").ToListAsync();
            res.Cursos = rut != null ? new() : cursos.Select(c => (c, s: Buscador.Puntaje(palabras, c.Nombre + " " + c.Letra + " " + c.Nombre.Replace(" ", "") + (c.Letra ?? ""))))
                .Where(x => x.s >= 3).OrderByDescending(x => x.s).ThenBy(x => Niveles.Orden(x.c.Nombre)).ThenBy(x => x.c.Letra)
                .Take(maxCursos).Select(x => x.c).ToList();

            // Asignaturas (una por nombre)
            if (rut == null)
            {
                var asig = await _context.Asignaturas.AsNoTracking().Include(a => a.Curso).Include(a => a.Docente)
                    .Where(a => a.Curso != null && (colegioId == 0 || a.Curso.ColegioId == colegioId) && a.Curso.AnioAcademico == anio).ToListAsync();
                res.Asignaturas = asig.Select(a => (a, s: Buscador.Puntaje(palabras, a.Nombre))).Where(x => x.s >= 3)
                    .OrderByDescending(x => x.s).ThenBy(x => Niveles.Orden(x.a.Curso!.Nombre)).Take(30).Select(x => x.a).ToList();
            }
        }

        // 2. Personal del colegio
        var personal = await _context.Usuarios.AsNoTracking()
            .Where(u => (colegioId == 0 || u.ColegioId == colegioId) && u.Activo && u.Rol != RolUsuario.Estudiante && u.Rol != RolUsuario.Apoderado)
            .ToListAsync();
        res.Personal = personal.Select(u => (u, s: P(u.Nombre + " " + u.Apellido, VeAlumnos() ? u.Rut : null)))
            .Where(x => x.s > 0).OrderByDescending(x => x.s).ThenBy(x => x.u.Apellido).Take(maxPersonal).Select(x => x.u).ToList();
        return res;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return View(new BusquedaResultadoViewModel { Query = string.Empty });
        ViewBag.VeFichas = VeFichas();
        return View(await BuscarAsync(query.Trim(), 60, 40, 12));
    }

    /// <summary>Sugerencias mientras se escribe (máximo 8), para el buscador de arriba.</summary>
    [HttpGet]
    public async Task<IActionResult> Sugerir(string? q)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return Json(Array.Empty<object>());
        var r = await BuscarAsync(q, 5, 4, 2);
        var fichas = VeFichas();
        var lista = new List<object>();
        lista.AddRange(r.Cursos.Select(c => new { i = "🏫", t = $"{c.Nombre} {c.Letra}".Trim(), s = $"Curso · {c.Estudiantes.Count} alumnos/as", u = fichas ? $"/Cursos/Alumnos/{c.Id}" : null }));
        lista.AddRange(r.Estudiantes.Select(e => new { i = "🎒", t = $"{e.Usuario!.Nombre} {e.Usuario.Apellido}", s = $"Alumno/a · {(e.Curso != null ? (e.Curso.Nombre + " " + e.Curso.Letra).Trim() : "sin curso")}", u = fichas ? $"/Cursos/Ficha/{e.Id}" : null }));
        // Quien administra el personal va a su ficha (editar, clave provisoria, horario); los demás, a su horario
        var admin = User.IsInRole("SuperAdmin") || User.IsInRole("Administrador") || User.IsInRole("Director") || User.IsInRole("UTP");
        lista.AddRange(r.Personal.Select(u => new { i = "👩‍🏫", t = $"{u.Nombre} {u.Apellido}", s = HorariosController.NombreRol(u.Rol) + (string.IsNullOrWhiteSpace(u.Asignaturas) ? "" : " · " + u.Asignaturas) + (admin ? " · editar o clave" : ""),
            u = admin ? $"/Personal/Editar/{u.Id}" : fichas ? $"/Horarios/Profesor/{u.Id}" : null }));
        return Json(lista.Take(9));
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
