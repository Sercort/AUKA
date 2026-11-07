using System.Security.Claims;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

/// <summary>
/// Clubes y talleres extraprogramáticos (ACLE): deportivos, artísticos, científicos, culturales...
/// Quién crea y quién inscribe lo decide el administrador del colegio (ver Services/Permisos.cs).
/// </summary>
[Authorize]
public class TalleresController : Controller
{
    public static readonly (string Clave, string Icono, string Chip)[] Categorias =
    {
        ("Deportivo", "⚽", "verde"), ("Artístico", "🎨", "coral"), ("Científico", "🔬", "teal"),
        ("Cultural", "🎭", "violeta"), ("Tecnológico", "💻", ""), ("Comunitario", "🤝", "sol")
    };

    public static (string Clave, string Icono, string Chip) Cat(string? c)
    {
        foreach (var x in Categorias) if (x.Clave == c) return x;
        return ("Otro", "🌟", "sol");
    }

    private readonly ApplicationDbContext _context;

    public TalleresController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int ColegioId() { _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var id); return id; }
    private int UsuarioId() { _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id); return id; }

    // 1. Todos los clubes del colegio. El docente ve primero los suyos; el alumno ve en cuáles está.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioId = ColegioId();
        var userId = UsuarioId();

        var talleres = await _context.Talleres
            .Include(t => t.Inscripciones)
            .Include(t => t.Docente)
            .Where(t => t.ColegioId == colegioId && t.Activo)
            .OrderBy(t => t.Nombre)
            .ToListAsync();

        if (User.IsInRole("Docente"))
            talleres = talleres.OrderByDescending(t => t.DocenteId == userId).ThenBy(t => t.Nombre).ToList();

        // Alumno/a: ¿en qué clubes está?
        var misClubes = new HashSet<int>();
        if (User.IsInRole("Estudiante"))
        {
            var est = await _context.Estudiantes.AsNoTracking().FirstOrDefaultAsync(e => e.UsuarioId == userId);
            if (est != null)
                misClubes = (await _context.InscripcionesTaller.AsNoTracking().Where(i => i.EstudianteId == est.Id).Select(i => i.TallerId).ToListAsync()).ToHashSet();
        }

        ViewBag.UserId = userId;
        ViewBag.MisClubes = misClubes;
        ViewBag.PuedeCrear = await Permisos.PuedeCrearClubesAsync(_context, User);
        return View(talleres);
    }

    // 2. Ficha del club: datos e integrantes
    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        var colegioId = ColegioId();
        var taller = await _context.Talleres.Include(t => t.Docente)
            .FirstOrDefaultAsync(t => t.Id == id && t.ColegioId == colegioId);
        if (taller == null) return NotFound();

        var integrantes = await _context.InscripcionesTaller.AsNoTracking()
            .Where(i => i.TallerId == id && i.Estudiante != null && i.Estudiante.Usuario != null)
            .Select(i => new IntegranteVm(i.Id, i.EstudianteId,
                i.Estudiante!.Usuario!.Nombre + " " + i.Estudiante.Usuario.Apellido,
                i.Estudiante.Usuario.Rut,
                i.Estudiante.Curso != null ? (i.Estudiante.Curso.Nombre + " " + i.Estudiante.Curso.Letra).Trim() : "",
                i.FechaUtc))
            .ToListAsync();

        ViewBag.PuedeInscribir = await Permisos.PuedeAsync(_context, User, Permisos.Inscribir, taller.DocenteId);
        // Cursos del año para inscribir por curso
        var anio = DateTime.Now.Year;
        ViewBag.Cursos = (await _context.Cursos.AsNoTracking()
                .Where(c => c.ColegioId == colegioId && c.Nombre != "Pacientes" && c.AnioAcademico == anio)
                .Select(c => new OpcionVm(c.Id, (c.Nombre + " " + c.Letra).Trim())).ToListAsync())
            .OrderBy(c => c.Nombre).ToList();
        ViewBag.Integrantes = integrantes.OrderBy(i => i.Curso).ThenBy(i => i.Nombre).ToList();
        // Alumnos y apoderados no ven la lista completa de integrantes (datos de otros niños)
        ViewBag.VeIntegrantes = !(User.IsInRole("Estudiante") || User.IsInRole("Apoderado"));
        return View(taller);
    }

    // 3. Buscar alumnos para inscribir (por nombre, apellido o RUT)
    [HttpGet]
    public async Task<IActionResult> BuscarAlumnos(int tallerId, string? q, int? cursoId)
    {
        var colegioId = ColegioId();
        var taller = await _context.Talleres.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tallerId && t.ColegioId == colegioId);
        if (taller == null || !await Permisos.PuedeAsync(_context, User, Permisos.Inscribir, taller.DocenteId)) return Forbid();
        q = (q ?? "").Trim().ToLower();
        if (q.Length < 2 && cursoId == null) return Json(Array.Empty<object>());
        var qRut = Rut.Normalizar(q);
        var buscaRut = qRut.Length >= 4 && qRut.Any(char.IsDigit);

        var inscritos = await _context.InscripcionesTaller.AsNoTracking().Where(i => i.TallerId == tallerId).Select(i => i.EstudianteId).ToListAsync();
        var lista = await _context.Estudiantes.AsNoTracking()
            .Where(e => e.ColegioId == colegioId && e.Usuario != null && e.Usuario.Activo && e.Curso != null && e.Curso.Nombre != "Pacientes"
                && (cursoId == null || e.CursoId == cursoId)
                && (q.Length < 2 || e.Usuario.Nombre.ToLower().Contains(q) || e.Usuario.Apellido.ToLower().Contains(q)
                    || (e.Usuario.Nombre + " " + e.Usuario.Apellido).ToLower().Contains(q)
                    || (buscaRut && e.Usuario.Rut.Replace(".", "").Replace("-", "").ToUpper().Contains(qRut))))
            .OrderBy(e => e.Usuario!.Apellido).ThenBy(e => e.Usuario!.Nombre)
            .Take(cursoId == null ? 12 : 60)
            .Select(e => new { id = e.Id, nombre = e.Usuario!.Nombre + " " + e.Usuario.Apellido, rut = e.Usuario.Rut, curso = (e.Curso!.Nombre + " " + e.Curso.Letra).Trim() })
            .ToListAsync();
        return Json(lista.Select(a => new { a.id, a.nombre, a.rut, a.curso, inscrito = inscritos.Contains(a.id) }));
    }

    // 4. Inscribir alumno/a
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inscribir(int tallerId, int estudianteId)
    {
        var colegioId = ColegioId();
        var taller = await _context.Talleres.FirstOrDefaultAsync(t => t.Id == tallerId && t.ColegioId == colegioId);
        if (taller == null) return Json(new { ok = false, error = "El club no existe." });
        if (!await Permisos.PuedeAsync(_context, User, Permisos.Inscribir, taller.DocenteId)) return Json(new { ok = false, error = "No tienes permiso para inscribir en este club." });
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId && e.ColegioId == colegioId)) return Json(new { ok = false, error = "Alumno/a no encontrado." });
        if (await _context.InscripcionesTaller.AnyAsync(i => i.TallerId == tallerId && i.EstudianteId == estudianteId)) return Json(new { ok = true, ya = true });
        var n = await _context.InscripcionesTaller.CountAsync(i => i.TallerId == tallerId);
        if (taller.Cupo is > 0 && n >= taller.Cupo) return Json(new { ok = false, error = $"El club ya completó su cupo ({taller.Cupo})." });

        _context.InscripcionesTaller.Add(new InscripcionTaller { ColegioId = colegioId, TallerId = tallerId, EstudianteId = estudianteId, FechaUtc = DateTime.UtcNow, InscritoPorId = UsuarioId() });
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // 5. Quitar alumno/a del club
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quitar(int id)
    {
        var colegioId = ColegioId();
        var ins = await _context.InscripcionesTaller.Include(i => i.Taller).FirstOrDefaultAsync(i => i.Id == id && i.ColegioId == colegioId);
        if (ins?.Taller == null) return NotFound();
        if (!await Permisos.PuedeAsync(_context, User, Permisos.Inscribir, ins.Taller.DocenteId)) return RedirectToAction("AccesoDenegado", "Account");
        _context.InscripcionesTaller.Remove(ins);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Alumno/a quitado del club.";
        return RedirectToAction(nameof(Detalle), new { id = ins.TallerId });
    }

    // 6. Crear club (GET)
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User)) return RedirectToAction("AccesoDenegado", "Account");
        await CargarDocentesAsync();
        return View(new Taller());
    }

    // 7. Crear club (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Taller taller)
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User)) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();

        ModelState.Remove(nameof(Taller.Docente));
        ModelState.Remove(nameof(Taller.Inscripciones));
        if (!Categorias.Any(c => c.Clave == taller.Categoria)) taller.Categoria = "Deportivo";
        if (taller.Cupo is <= 0) taller.Cupo = null;

        Usuario? docente = null;
        if (taller.DocenteId == null)
            ModelState.AddModelError(nameof(Taller.DocenteId), "Elige al profesor/a a cargo.");
        else
        {
            docente = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == taller.DocenteId && u.Rol == RolUsuario.Docente && u.Activo && u.ColegioId == colegioId);
            if (docente == null) ModelState.AddModelError(nameof(Taller.DocenteId), "El profesor/a elegido no es válido.");
        }

        if (ModelState.IsValid && docente != null)
        {
            taller.ColegioId = colegioId;
            taller.DocenteCargo = $"{docente.Nombre} {docente.Apellido}";
            _context.Talleres.Add(taller);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Club «{taller.Nombre}» creado. Ya puedes sumar integrantes.";
            return RedirectToAction(nameof(Detalle), new { id = taller.Id });
        }

        await CargarDocentesAsync(taller.DocenteId);
        return View(taller);
    }

    private async Task CargarDocentesAsync(int? docenteSeleccionado = null)
    {
        var colegioId = ColegioId();
        var docentes = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Docente && u.Activo && u.ColegioId == colegioId)
            .OrderBy(u => u.Apellido)
            .Select(u => new { u.Id, NombreCompleto = u.Nombre + " " + u.Apellido })
            .ToListAsync();
        ViewBag.Docentes = new SelectList(docentes, "Id", "NombreCompleto", docenteSeleccionado);
    }
}

public record IntegranteVm(int Id, int EstudianteId, string Nombre, string Rut, string Curso, DateTime FechaUtc);
