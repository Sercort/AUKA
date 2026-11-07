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

    // 3. Importar anotaciones desde Excel (CSV)
    /// <summary>
    /// Columnas: RUT alumno; Tipo (Positiva/Negativa); Detalle; Fecha (opcional); RUT profesor (opcional).
    /// Se cruza por RUT. Si la misma anotación (alumno/a, tipo, detalle y día) ya existe, no se duplica.
    /// No envía avisos a apoderados/as: es para traer el historial, no para notificar.
    /// </summary>
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Importar(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0) { TempData["ErrorMessage"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var pl = await Auka.Web.Services.Csv.LeerAsync(archivo);
        int iRut = pl.Col("rut alumno", "rut estudiante", "rut"), iTipo = pl.Col("tipo"), iDet = pl.Col("detalle", "descripcion", "anotacion", "observacion"),
            iFec = pl.Col("fecha"), iProf = pl.Col("rut profesor", "rut docente", "rut funcionario");
        if (iRut < 0 || iTipo < 0 || iDet < 0) { TempData["ErrorMessage"] = "Faltan columnas. Usa la plantilla: RUT alumno, Tipo y Detalle."; return RedirectToAction(nameof(Index)); }
        if (pl.Filas.Count > 20000) { TempData["ErrorMessage"] = "Máximo 20.000 anotaciones por archivo."; return RedirectToAction(nameof(Index)); }

        _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var yo);
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colegioId);
        var anio = DateTime.Now.Year;
        var estudiantes = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null && e.Curso.AnioAcademico == anio).ToListAsync();
        var porRut = estudiantes.GroupBy(e => Auka.Web.Services.Rut.Normalizar(e.Usuario!.Rut)).ToDictionary(g => g.Key, g => g.First());
        var personal = (await _context.Usuarios.AsNoTracking()
                .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol != RolUsuario.Estudiante && u.Rol != RolUsuario.Apoderado)
                .Select(u => new { u.Id, u.Rut }).ToListAsync())
            .GroupBy(u => Auka.Web.Services.Rut.Normalizar(u.Rut)).ToDictionary(g => g.Key, g => g.First().Id);

        var ids = estudiantes.Select(e => e.Id).ToList();
        var ya = (await _context.Anotaciones.AsNoTracking().Where(a => ids.Contains(a.EstudianteId))
                .Select(a => new { a.EstudianteId, a.Tipo, a.Detalle, a.Fecha }).ToListAsync())
            .Select(a => (a.EstudianteId, a.Tipo, Auka.Web.Services.Csv.Clave(a.Detalle), a.Fecha.Date)).ToHashSet();

        var errores = new List<string>();
        int nuevas = 0, repetidas = 0;
        for (int i = 0; i < pl.Filas.Count; i++)
        {
            var f = pl.Filas[i];
            var fila = i + 2;
            string C(int k) => Auka.Web.Services.Csv.Planilla.Celda(f, k);
            if (C(iRut).Length == 0 && C(iDet).Length == 0) continue;
            if (!Auka.Web.Services.Rut.TryValidar(C(iRut), out var rut, out _)) { errores.Add($"Fila {fila}: RUT no válido ({C(iRut)})."); continue; }
            if (!porRut.TryGetValue(rut, out var est)) { errores.Add($"Fila {fila}: no hay alumno/a con RUT {C(iRut)} este año."); continue; }
            var t = Auka.Web.Services.Csv.Clave(C(iTipo));
            TipoAnotacion tipo;
            if (t.StartsWith("pos") || t == "+" || t == "p") tipo = TipoAnotacion.Positiva;
            else if (t.StartsWith("neg") || t == "-" || t == "n") tipo = TipoAnotacion.Negativa;
            else { errores.Add($"Fila {fila}: el tipo debe ser Positiva o Negativa ({C(iTipo)})."); continue; }
            var detalle = C(iDet);
            if (detalle.Length == 0) { errores.Add($"Fila {fila}: falta el detalle."); continue; }
            if (detalle.Length > 1000) detalle = detalle[..1000];
            var fecha = (iFec >= 0 ? Auka.Web.Services.Csv.Fecha(C(iFec)) : null) ?? DateTime.UtcNow;
            var clave = (est.Id, tipo, Auka.Web.Services.Csv.Clave(detalle), fecha.Date);
            if (!ya.Add(clave)) { repetidas++; continue; }

            var autor = yo;
            if (iProf >= 0 && Auka.Web.Services.Rut.TryValidar(C(iProf), out var rp, out _) && personal.TryGetValue(rp, out var idProf)) autor = idProf;
            _context.Anotaciones.Add(new Anotacion { EstudianteId = est.Id, DocenteId = autor, Tipo = tipo, Detalle = detalle, Fecha = fecha });
            nuevas++;
            if (nuevas % 1000 == 0) await _context.SaveChangesAsync();
        }
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"📥 Anotaciones importadas: {nuevas} nuevas" + (repetidas > 0 ? $", {repetidas} ya estaban (no se duplicaron)" : "") + ".";
        if (errores.Count > 0) TempData["ErrorMessage"] = $"{errores.Count} {(errores.Count == 1 ? "fila no se importó" : "filas no se importaron")}: " + string.Join(" ", errores.Take(8)) + (errores.Count > 8 ? " …" : "");
        return RedirectToAction(nameof(Index));
    }
}

public record CursoAnotVm(int Id, string Nombre, string Letra, int Alumnos, int Positivas30, int Negativas30);
public record AlumnoAnotVm(int Id, string Nombre, string Apellido, string Rut, int Positivas, int Negativas);
