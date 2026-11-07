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
using Auka.Web.Services;

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
    public async Task<IActionResult> Index(int? anio)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var cursos = await _context.Cursos
            .Include(c => c.ProfesorJefe)
            .Include(c => c.Estudiantes)
            .Where(c => (c.ColegioId == colegioId || colegioId == 0) && c.Nombre != "Pacientes")
            .ToListAsync();

        // Archivador de años escolares: el año actual cambia solo (en 2027 se abre 2027); los anteriores quedan para consulta
        var actual = DateTime.Now.Year;
        var anios = cursos.Select(c => c.AnioAcademico).Append(actual).Distinct().OrderByDescending(a => a).ToList();
        var elegido = anio != null && anios.Contains(anio.Value) ? anio.Value
            : (cursos.Any(c => c.AnioAcademico == actual) ? actual : (cursos.Count > 0 ? cursos.Max(c => c.AnioAcademico) : actual));
        ViewBag.Anio = elegido;
        ViewBag.Anios = anios;
        ViewBag.AnioActual = actual;

        var delAnio = cursos.Where(c => c.AnioAcademico == elegido).ToList();
        // Años anteriores: los alumnos/as se cuentan desde el historial (ya pasaron de curso)
        var ids = delAnio.Select(c => c.Id).ToList();
        ViewBag.Conteo = elegido == actual
            ? delAnio.ToDictionary(c => c.Id, c => c.Estudiantes.Count)
            : (await _context.MatriculasAnuales.AsNoTracking().Where(m => ids.Contains(m.CursoId)).GroupBy(m => m.CursoId)
                .Select(g => new { g.Key, N = g.Count() }).ToListAsync()).ToDictionary(x => x.Key, x => x.N);
        return View(delAnio
            .OrderBy(c => OrdenNivel(c.Nombre) < 0 ? 99 : OrdenNivel(c.Nombre)).ThenBy(c => c.Nombre).ThenBy(c => c.Letra).ToList());
    }

    // ---------------------------------------------------------------- Ficha del alumno/a
    // Todo lo del alumno/a en una página: datos, apoderados, asistencia, anotaciones, clubes, matrícula, documentos e historial.
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Inspector,Orientadora")]
    [HttpGet]
    public async Task<IActionResult> Ficha(int id)
    {
        var colegioId = ColegioActual();
        var e = await _context.Estudiantes.AsNoTracking()
            .Include(x => x.Usuario).Include(x => x.Curso).ThenInclude(c => c!.ProfesorJefe)
            .Include(x => x.Apoderados).ThenInclude(a => a.Apoderado).ThenInclude(a => a!.Usuario)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && x.ColegioId == colegioId);
        if (e?.Usuario == null) return NotFound();

        var anio = e.Curso?.AnioAcademico ?? DateTime.Now.Year;
        var ini = new DateTime(anio, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var asis = await _context.Asistencias.AsNoTracking().Where(a => a.EstudianteId == id && a.Fecha >= ini).ToListAsync();
        var anot = await _context.Anotaciones.AsNoTracking().Where(a => a.EstudianteId == id && a.Fecha >= ini).OrderByDescending(a => a.Fecha).ToListAsync();
        ViewBag.AsisP = asis.Count(a => a.Presente);
        ViewBag.AsisT = asis.Count;
        ViewBag.Anotaciones = anot;
        ViewBag.Clubes = await _context.InscripcionesTaller.AsNoTracking().Where(i => i.EstudianteId == id && i.Taller != null)
            .Select(i => new OpcionVm(i.TallerId, i.Taller!.Nombre)).ToListAsync();
        ViewBag.Historial = await _context.MatriculasAnuales.AsNoTracking().Include(m => m.Curso).Where(m => m.EstudianteId == id)
            .OrderByDescending(m => m.Anio).ToListAsync();
        ViewBag.PuedeClave = User.IsInRole("SuperAdmin") || User.IsInRole("Director") || User.IsInRole("UTP") || User.IsInRole("Inspector");
        return View(e);
    }

    // Clave provisoria: cuando el alumno/a o su apoderado/a pierde la contraseña y lo pide en el colegio.
    // Se muestra una sola vez y obliga a cambiarla al entrar.
    [Authorize(Roles = "SuperAdmin,Director,UTP,Inspector")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClaveProvisoria(int estudianteId, int usuarioId)
    {
        var colegioId = ColegioActual();
        var e = await _context.Estudiantes.AsNoTracking().Include(x => x.Apoderados).ThenInclude(a => a.Apoderado)
            .FirstOrDefaultAsync(x => x.Id == estudianteId && x.ColegioId == colegioId);
        if (e == null) return NotFound();
        // Solo el propio alumno/a o uno de sus apoderados/as
        var permitido = usuarioId == e.UsuarioId || e.Apoderados.Any(a => a.Apoderado != null && a.Apoderado.UsuarioId == usuarioId);
        var u = permitido ? await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.ColegioId == colegioId) : null;
        if (u == null) return NotFound();

        var clave = GenerarClave();
        u.PasswordHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        u.DebeCambiarPassword = true;
        u.Activo = true;
        await _context.SaveChangesAsync();
        var usuarioLogin = u.Email.EndsWith("auka.cl", StringComparison.OrdinalIgnoreCase) ? $"su RUT ({u.Rut})" : u.Email;
        TempData["ClaveProvisoria"] = $"Clave provisoria de {u.Nombre} {u.Apellido}: {clave}  ·  Entra con {usuarioLogin}. Al ingresar, AUKA le pedirá crear una nueva.";
        return RedirectToAction(nameof(Ficha), new { id = estudianteId });
    }

    // Documentos pendientes de todo el colegio (traslados): quién debe qué documento
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpGet]
    public async Task<IActionResult> Pendientes(int? cursoId)
    {
        var colegioId = ColegioActual();
        var lista = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.ColegioId == colegioId && e.DocumentosPendientes != null && e.DocumentosPendientes != "" && (cursoId == null || e.CursoId == cursoId))
            .ToListAsync();
        ViewBag.CursoId = cursoId;
        return View(lista.OrderBy(e => OrdenNivel(e.Curso?.Nombre ?? "")).ThenBy(e => e.Curso?.Letra).ThenBy(e => e.Usuario?.Apellido).ToList());
    }

    // Marca un documento como entregado (sale de la lista de pendientes)
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarEntregado(int estudianteId, string documento, int? cursoId)
    {
        var colegioId = ColegioActual();
        var e = await _context.Estudiantes.FirstOrDefaultAsync(x => x.Id == estudianteId && x.ColegioId == colegioId);
        if (e != null)
        {
            var resto = (e.DocumentosPendientes ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(d => !string.Equals(d.Trim(), documento?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            e.DocumentosPendientes = resto.Count == 0 ? null : string.Join("|", resto);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"«{documento}» marcado como entregado.";
        }
        return RedirectToAction(nameof(Pendientes), new { cursoId });
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
            .Include(c => c.Estudiantes).ThenInclude(e => e.Usuario)
            .Include(c => c.Estudiantes).ThenInclude(e => e.Apoderados).ThenInclude(a => a.Apoderado).ThenInclude(a => a!.Usuario)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id && (c.ColegioId == colegioId || colegioId == 0));

        if (curso == null) return NotFound();

        // Año actual: los alumnos/as del curso. Años anteriores: los que estuvieron en él según el historial.
        var esArchivo = curso.AnioAcademico < DateTime.Now.Year;
        var alumnos = curso.Estudiantes.ToList();
        if (esArchivo)
        {
            var idsHist = await _context.MatriculasAnuales.AsNoTracking().Where(m => m.CursoId == curso.Id).Select(m => m.EstudianteId).ToListAsync();
            alumnos = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Apoderados).ThenInclude(a => a.Apoderado).ThenInclude(a => a!.Usuario)
                .Where(e => idsHist.Contains(e.Id)).AsSplitQuery().ToListAsync();
        }
        ViewBag.Alumnos = alumnos;
        ViewBag.EsArchivo = esArchivo;

        // Asistencia del año (presentes / registros) y clubes de cada alumno/a
        var idsAl = alumnos.Select(e => e.Id).ToList();
        var ini = new DateTime(curso.AnioAcademico, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fin = ini.AddYears(1);
        ViewBag.Asistencia = (await _context.Asistencias.AsNoTracking()
                .Where(a => idsAl.Contains(a.EstudianteId) && a.Fecha >= ini && a.Fecha < fin)
                .GroupBy(a => a.EstudianteId)
                .Select(g => new { g.Key, P = g.Count(x => x.Presente), T = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => (x.P, x.T));
        ViewBag.Clubes = (await _context.InscripcionesTaller.AsNoTracking()
                .Where(i => idsAl.Contains(i.EstudianteId) && i.Taller != null && i.Taller.Activo)
                .Select(i => new { i.EstudianteId, i.Taller!.Nombre }).ToListAsync())
            .GroupBy(x => x.EstudianteId).ToDictionary(g => g.Key, g => g.Select(x => x.Nombre).ToList());

        var otrosCursos = await _context.Cursos
            .Where(c => c.ColegioId == colegioId && c.Id != curso.Id && c.AnioAcademico == curso.AnioAcademico)
            .OrderBy(c => c.Nombre).ThenBy(c => c.Letra)
            .ToListAsync();

        ViewBag.OtrosCursos = otrosCursos;
        ViewBag.PuedeProfJefe = await Permisos.PuedeAsync(_context, User, Permisos.ProfJefe);
        ViewBag.Docentes = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
            .OrderBy(u => u.Apellido).Select(u => new OpcionVm(u.Id, u.Nombre + " " + u.Apellido)).ToListAsync();
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
            .Where(c => (c.ColegioId == colegioId || colegioId == 0) && c.Nombre != "Pacientes")
            .Select(c => c.Nombre)
            .Distinct()
            .ToListAsync();

        // Niveles oficiales en orden + los que el colegio ya tenga con otro nombre (sin repetir el mismo nivel escrito distinto)
        var todos = NivelesOficiales.ToList();
        todos.AddRange(nivelesEnBD.Where(n => OrdenNivel(n) < 0 && !todos.Any(t => Normal(t) == Normal(n))));

        ViewBag.Niveles = new SelectList(todos);
        // Desde septiembre se puede matricular también para el año siguiente
        var hoy = DateTime.Now;
        ViewBag.AniosMatricula = hoy.Month >= 9 ? new[] { hoy.Year, hoy.Year + 1 } : new[] { hoy.Year };
        ViewBag.Especialidades = await EspecialidadesAsync(colegioId);
        return View();
    }

    // 5a. Revisión en vivo al escribir un RUT en la matrícula: ¿ya existe el alumno o el apoderado en este colegio?
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpGet]
    public async Task<IActionResult> BuscarPersona(string rut, string tipo)
    {
        if (!Rut.TryValidar(rut, out var norm, out var fmt)) return Json(new { valido = false });
        var colegioId = ColegioActual();
        var rol = tipo == "apoderado" ? RolUsuario.Apoderado : RolUsuario.Estudiante;
        var candidatos = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Rol == rol)
            .Select(u => new { u.Id, u.Rut, u.Nombre, u.Apellido, u.Email, u.Telefono })
            .ToListAsync();
        var u = candidatos.FirstOrDefault(x => Rut.Normalizar(x.Rut) == norm);
        if (u == null) return Json(new { valido = true, existe = false, rut = fmt });

        if (rol == RolUsuario.Estudiante)
        {
            var est = await _context.Estudiantes.AsNoTracking().Include(e => e.Curso).FirstOrDefaultAsync(e => e.UsuarioId == u.Id);
            // Apoderados del alumno (titular primero) para completar solos el formulario
            var apos = est == null ? new List<ApoderadoDato>() : await _context.EstudiantesApoderados.AsNoTracking()
                .Where(x => x.EstudianteId == est.Id && x.Apoderado != null && x.Apoderado.Usuario != null)
                .OrderByDescending(x => x.EsSostenedorPrincipal)
                .Select(x => new ApoderadoDato(x.Apoderado!.Usuario!.Rut, x.Apoderado.Usuario.Nombre, x.Apoderado.Usuario.Apellido,
                    x.Apoderado.Usuario.Email, x.Apoderado.TelefonoContacto, x.Parentesco))
                .ToListAsync();
            var nivelActual = est?.Curso?.Nombre;
            return Json(new { valido = true, existe = true, rut = fmt, nombre = u.Nombre, apellido = u.Apellido, email = u.Email,
                curso = est?.Curso == null ? null : (est.Curso.Nombre + " " + est.Curso.Letra).Trim(),
                anio = est?.Curso?.AnioAcademico,
                nivelSiguiente = nivelActual == null ? null : NivelSiguiente(nivelActual),
                especialidad = est?.Especialidad,
                apoderados = apos });
        }
        var apo = await _context.Apoderados.AsNoTracking().FirstOrDefaultAsync(a => a.UsuarioId == u.Id);
        var hijos = apo == null ? new List<string>() : await _context.EstudiantesApoderados.AsNoTracking()
            .Where(x => x.ApoderadoId == apo.Id && x.Estudiante != null && x.Estudiante.Usuario != null)
            .Select(x => x.Estudiante!.Usuario!.Nombre + " " + x.Estudiante.Usuario.Apellido + (x.Estudiante.Curso != null ? " (" + x.Estudiante.Curso.Nombre + " " + x.Estudiante.Curso.Letra + ")" : ""))
            .ToListAsync();
        return Json(new { valido = true, existe = true, rut = fmt, nombre = u.Nombre, apellido = u.Apellido, email = u.Email,
            telefono = apo?.TelefonoContacto ?? u.Telefono ?? "", hijos });
    }

    public record ApoderadoDato(string Rut, string Nombre, string Apellido, string Email, string Telefono, string Parentesco);

    // ---------------- Niveles y especialidades ----------------
    public static readonly string[] NivelesOficiales =
    {
        "Prekínder", "Kínder",
        "1º Básico", "2º Básico", "3º Básico", "4º Básico", "5º Básico", "6º Básico", "7º Básico", "8º Básico",
        "1º Medio", "2º Medio", "3º Medio", "4º Medio"
    };

    public static readonly string[] DocumentosTraslado =
    {
        "Certificado de notas o de estudios", "Informe de personalidad", "Certificado de nacimiento", "Informe PIE o de especialista"
    };

    // "5° básico", "5º Básico" y "5 basico" son el mismo nivel
    private static string Normal(string n)
    {
        var x = (n ?? "").Trim().ToLowerInvariant().Replace("°", "º").Replace("í", "i").Replace("á", "a").Replace("é", "e");
        // "III° Medio" (romanos) = "3º Medio"
        var rom = System.Text.RegularExpressions.Regex.Match(x, @"^(iv|iii|ii|i)\s*º?\s+medio");
        if (rom.Success) x = (rom.Groups[1].Value switch { "i" => "1", "ii" => "2", "iii" => "3", _ => "4" }) + "º medio" + x[rom.Length..];
        x = System.Text.RegularExpressions.Regex.Replace(x, @"^(\d)\s*º?\s*", "$1º ");
        x = x.Replace("pre-kinder", "prekinder").Replace("pre kinder", "prekinder");
        return System.Text.RegularExpressions.Regex.Replace(x, @"\s+", " ");
    }

    private static int OrdenNivel(string n) => Array.FindIndex(NivelesOficiales, o => Normal(o) == Normal(n));

    /// <summary>Nivel del año siguiente (para renovación). null si egresa (4º medio) o si no es un nivel conocido.</summary>
    public static string? NivelSiguiente(string actual)
    {
        var i = OrdenNivel(actual);
        return i < 0 || i + 1 >= NivelesOficiales.Length ? null : NivelesOficiales[i + 1];
    }

    public static bool EsMedioTP(string? nivel)
    {
        var x = Normal(nivel ?? "");
        return x.StartsWith("3º medio") || x.StartsWith("4º medio");
    }

    private async Task<List<string>> EspecialidadesAsync(int colegioId)
    {
        var texto = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == colegioId).Select(c => c.Especialidades).FirstOrDefaultAsync();
        return (texto ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(e => e).ToList();
    }

    // Asigna (o quita) el profesor jefe de un curso. Pueden: administrador siempre; UTP o director si el administrador lo permitió.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AsignarProfesorJefe(int cursoId, int usuarioId, string? volver)
    {
        if (!await Permisos.PuedeAsync(_context, User, Permisos.ProfJefe)) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioActual();
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId && c.ColegioId == colegioId);
        if (curso == null) return NotFound();
        if (usuarioId == 0) curso.ProfesorJefeId = null;
        else
        {
            var ok = await _context.Usuarios.AnyAsync(u => u.Id == usuarioId && u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente);
            if (!ok) { TempData["Error"] = "El profesor elegido no es válido."; return LocalRedirect(Url.IsLocalUrl(volver) ? volver! : "/"); }
            curso.ProfesorJefeId = usuarioId;
        }
        await _context.SaveChangesAsync();
        TempData["PanelOk"] = TempData["SuccessMessage"] = usuarioId == 0 ? $"Se quitó el profesor jefe de {curso.Nombre} {curso.Letra}." : $"Profesor jefe de {curso.Nombre} {curso.Letra} actualizado.";
        return LocalRedirect(Url.IsLocalUrl(volver) ? volver! : "/");
    }

    private static string SiguienteLetra(IEnumerable<Curso> delNivel)
    {
        var usadas = delNivel.Select(c => (c.Letra ?? "").Trim().ToUpperInvariant()).ToHashSet();
        for (var l = 'A'; l <= 'Z'; l++) if (!usadas.Contains(l.ToString())) return l.ToString();
        return (usadas.Count + 1).ToString();
    }

    // Agrega a mano la siguiente letra de un nivel (ej: ya existen 1º Básico A y B → crea 1º Básico C)
    [Authorize(Roles = "SuperAdmin,Director,UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarLetra(string nivel)
    {
        var colegioId = ColegioActual();
        var anio = DateTime.Now.Year;
        if (string.IsNullOrWhiteSpace(nivel)) return RedirectToAction(nameof(Index));
        var delNivel = (await _context.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync())
            .Where(c => Normal(c.Nombre) == Normal(nivel)).ToList();
        var nuevo = new Curso
        {
            Nombre = delNivel.FirstOrDefault()?.Nombre ?? nivel.Trim(),
            Letra = SiguienteLetra(delNivel),
            CapacidadMaxima = delNivel.FirstOrDefault()?.CapacidadMaxima ?? 40,
            ColegioId = colegioId, AnioAcademico = anio
        };
        _context.Cursos.Add(nuevo);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Curso {nuevo.Nombre} {nuevo.Letra} creado.";
        return Redirect(Url.Action(nameof(Index)) + "?abrir=" + Uri.EscapeDataString(nuevo.Nombre));
    }

    private int ColegioActual()
    {
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colegioId);
        return colegioId;
    }

    private static string GenerarClave()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alfabeto[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alfabeto.Length)]).ToArray());
    }

    private static string? Limpio(string? t, int max)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        t = t.Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static bool EsCorreoReal(string? c) =>
        !string.IsNullOrWhiteSpace(c) && System.Text.RegularExpressions.Regex.IsMatch(c, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")
        && !c.EndsWith("auka.cl", StringComparison.OrdinalIgnoreCase);   // los @...auka.cl son usuarios internos, sin correo real

    // 5. Procesar Matrícula (POST)
    //    - Si el alumno ya existe en el colegio (mismo RUT), se usa su cuenta y se actualiza el curso.
    //    - Si el apoderado ya existe (mismo RUT), se le suma este alumno: queda con todos sus hijos.
    //    - Si no existen, se crean sus cuentas. Al apoderado con correo real le llega un enlace para crear su contraseña.
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Matricular(
        string rut, string nombre, string apellido, string? email, string nivel, TipoMatricula tipoMatricula,
        string rutApoderado, string nombreApoderado, string apellidoApoderado, string? emailApoderado,
        string? telefonoContacto, string? parentesco,
        string? especialidad, string? especialidadNueva,
        string? colegioOrigen, string? rbdOrigen, string? comunaOrigen, DateOnly? fechaTraslado, string? motivoTraslado,
        bool necesidadesEducativas, string[]? documentosEntregados, int? anioEscolar,
        [FromServices] IAccesoService acceso)
    {
        var colegioId = ColegioActual();
        if (!Rut.TryValidar(rut, out var rutEstNorm, out var rutEstFmt)) { TempData["Error"] = "El RUT del alumno no es válido."; return RedirectToAction(nameof(Matricular)); }
        if (!Rut.TryValidar(rutApoderado, out var rutApoNorm, out var rutApoFmt)) { TempData["Error"] = "El RUT del apoderado no es válido."; return RedirectToAction(nameof(Matricular)); }
        if (rutEstNorm == rutApoNorm) { TempData["Error"] = "El alumno y el apoderado no pueden tener el mismo RUT."; return RedirectToAction(nameof(Matricular)); }
        if (tipoMatricula == TipoMatricula.Traslado && string.IsNullOrWhiteSpace(colegioOrigen)) { TempData["Error"] = "En un traslado, indica de qué colegio viene el alumno/a."; return RedirectToAction(nameof(Matricular)); }

        var hasher = new PasswordHasher<string>();
        var avisos = new List<string>();
        try
        {
            // A) Curso: el del nivel elegido con menos alumnos (o se crea)
            var nivelLimpio = string.IsNullOrWhiteSpace(nivel) ? "General" : nivel.Trim();
            var anio = DateTime.Now.Year;
            if (anioEscolar == anio + 1 && DateTime.Now.Month >= 9) anio = anioEscolar.Value;   // matrícula para el año siguiente
            // Mismo nivel aunque esté escrito distinto ("5° Básico" = "5º Básico")
            var delNivel = (await _context.Cursos.Include(c => c.Estudiantes)
                    .Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync())
                .Where(c => Normal(c.Nombre) == Normal(nivelLimpio)).ToList();
            var curso = delNivel.OrderBy(c => c.Estudiantes.Count).ThenBy(c => c.Letra).FirstOrDefault();
            // Sin cursos en el nivel, o todos llenos: se crea solo el siguiente (A, B, C...)
            if (curso == null || curso.Estudiantes.Count >= curso.CapacidadMaxima)
            {
                var nuevo = new Curso { Nombre = curso?.Nombre ?? nivelLimpio, Letra = SiguienteLetra(delNivel), CapacidadMaxima = curso?.CapacidadMaxima ?? 40, ColegioId = colegioId, AnioAcademico = anio };
                _context.Cursos.Add(nuevo);
                await _context.SaveChangesAsync();
                if (curso != null) avisos.Add($"Los cursos de {nuevo.Nombre} estaban llenos: se creó {nuevo.Nombre} {nuevo.Letra}.");
                curso = nuevo;
            }

            // B) Alumno: ¿ya existe en este colegio?
            var alumnos = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Rol == RolUsuario.Estudiante).ToListAsync();
            var uEst = alumnos.FirstOrDefault(u => Rut.Normalizar(u.Rut) == rutEstNorm);
            string? claveAlumno = null;
            if (uEst == null)
            {
                var correoEst = EsCorreoReal(email) ? email!.Trim().ToLowerInvariant() : $"{rutEstNorm.ToLowerInvariant()}@alumnos.auka.cl";
                if (await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == correoEst))
                    correoEst = $"{rutEstNorm.ToLowerInvariant()}@alumnos.auka.cl";
                claveAlumno = GenerarClave();
                uEst = new Usuario
                {
                    Rut = rutEstFmt, Nombre = nombre.Trim(), Apellido = apellido.Trim(), Email = correoEst,
                    Rol = RolUsuario.Estudiante, ColegioId = colegioId, Activo = true, DebeCambiarPassword = true, FechaCreacion = DateTime.UtcNow
                };
                uEst.PasswordHash = hasher.HashPassword(correoEst, claveAlumno);
                _context.Usuarios.Add(uEst);
                await _context.SaveChangesAsync();
                avisos.Add($"Alumno/a nuevo/a: {uEst.Nombre} {uEst.Apellido} (usuario {uEst.Email}).");
            }
            else avisos.Add($"El alumno/a {uEst.Nombre} {uEst.Apellido} ya existía: se actualizó su matrícula.");

            var estudiante = await _context.Estudiantes.FirstOrDefaultAsync(e => e.UsuarioId == uEst.Id);
            var accesoPropio = !Citaciones.EsCursoMenor(curso.Nombre);   // kínder a 6° básico: lo maneja el apoderado
            if (estudiante == null)
            {
                estudiante = new Estudiante { UsuarioId = uEst.Id, CursoId = curso.Id, ColegioId = colegioId, TipoMatricula = tipoMatricula, FechaMatricula = DateTime.UtcNow, AccesoPropio = accesoPropio };
                _context.Estudiantes.Add(estudiante);
            }
            else
            {
                estudiante.CursoId = curso.Id;
                estudiante.TipoMatricula = tipoMatricula;
                estudiante.AccesoPropio = accesoPropio;
            }
            // Historial: en qué curso queda este año escolar (para el archivo de años anteriores)
            if (estudiante.Id == 0) await _context.SaveChangesAsync();
            var hist = await _context.MatriculasAnuales.FirstOrDefaultAsync(m => m.EstudianteId == estudiante.Id && m.Anio == curso.AnioAcademico);
            if (hist == null) _context.MatriculasAnuales.Add(new MatriculaAnual { ColegioId = colegioId, EstudianteId = estudiante.Id, CursoId = curso.Id, Anio = curso.AnioAcademico, Tipo = tipoMatricula, FechaUtc = DateTime.UtcNow });
            else { hist.CursoId = curso.Id; hist.Tipo = tipoMatricula; }

            // Especialidad (3º y 4º medio TP). Si es nueva, queda guardada en la lista del colegio para las próximas matrículas.
            var esp = Limpio(string.IsNullOrWhiteSpace(especialidadNueva) ? especialidad : especialidadNueva, 80);
            if (EsMedioTP(curso.Nombre) && esp != null)
            {
                estudiante.Especialidad = esp;
                var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
                var lista = (col?.Especialidades ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (col != null && !lista.Any(e => string.Equals(e, esp, StringComparison.OrdinalIgnoreCase)))
                {
                    lista.Add(esp);
                    col.Especialidades = string.Join("|", lista);
                    avisos.Add($"Nueva especialidad guardada: {esp}.");
                }
            }
            else if (!EsMedioTP(curso.Nombre)) estudiante.Especialidad = null;

            // Traslado: de dónde viene y qué documentos faltan (lo revisa la UTP)
            if (tipoMatricula == TipoMatricula.Traslado)
            {
                estudiante.ColegioOrigen = Limpio(colegioOrigen, 160);
                estudiante.RbdOrigen = Limpio(rbdOrigen, 20);
                estudiante.ComunaOrigen = Limpio(comunaOrigen, 80);
                estudiante.FechaTraslado = fechaTraslado ?? DateOnly.FromDateTime(DateTime.Now);
                estudiante.MotivoTraslado = Limpio(motivoTraslado, 300);
                estudiante.NecesidadesEducativas = necesidadesEducativas;
                var entregados = documentosEntregados ?? Array.Empty<string>();
                var pendientes = DocumentosTraslado
                    .Where(d => necesidadesEducativas || !d.StartsWith("Informe PIE"))
                    .Where(d => !entregados.Contains(d)).ToList();
                estudiante.DocumentosPendientes = pendientes.Count == 0 ? null : string.Join("|", pendientes);
                if (pendientes.Count > 0) avisos.Add($"Documentos pendientes: {string.Join(", ", pendientes)}.");
            }
            await _context.SaveChangesAsync();
            if (!accesoPropio) { claveAlumno = null; avisos.Add("Por su curso, el alumno/a no usa la plataforma: todo lo recibe su apoderado/a."); }

            // C) Apoderado: ¿ya existe en este colegio? Si existe, se le suma este hijo/a
            var apoderados = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Rol == RolUsuario.Apoderado).ToListAsync();
            var uApo = apoderados.FirstOrDefault(u => Rut.Normalizar(u.Rut) == rutApoNorm);
            string? claveApo = null; var apoNuevo = false;
            if (uApo == null)
            {
                var correoApo = EsCorreoReal(emailApoderado) ? emailApoderado!.Trim().ToLowerInvariant() : null;
                if (correoApo != null && await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == correoApo))
                {
                    avisos.Add($"El correo {correoApo} ya pertenece a otra cuenta de AUKA: al apoderado/a se le creó un usuario con su RUT.");
                    correoApo = null;
                }
                uApo = new Usuario
                {
                    Rut = rutApoFmt,
                    Nombre = string.IsNullOrWhiteSpace(nombreApoderado) ? "Apoderado/a" : nombreApoderado.Trim(),
                    Apellido = apellidoApoderado?.Trim() ?? "",
                    Email = correoApo ?? $"{rutApoNorm.ToLowerInvariant()}@apoderados.auka.cl",
                    Telefono = telefonoContacto?.Trim(),
                    Rol = RolUsuario.Apoderado, ColegioId = colegioId, Activo = true, DebeCambiarPassword = true, FechaCreacion = DateTime.UtcNow
                };
                claveApo = GenerarClave();
                uApo.PasswordHash = hasher.HashPassword(uApo.Email, claveApo);
                _context.Usuarios.Add(uApo);
                await _context.SaveChangesAsync();
                apoNuevo = true;
            }
            var apo = await _context.Apoderados.FirstOrDefaultAsync(a => a.UsuarioId == uApo.Id);
            if (apo == null) { apo = new Apoderado { UsuarioId = uApo.Id, TelefonoContacto = telefonoContacto?.Trim() ?? "" }; _context.Apoderados.Add(apo); await _context.SaveChangesAsync(); }
            else if (string.IsNullOrWhiteSpace(apo.TelefonoContacto) && !string.IsNullOrWhiteSpace(telefonoContacto)) apo.TelefonoContacto = telefonoContacto.Trim();

            var yaVinculado = await _context.EstudiantesApoderados.AnyAsync(x => x.EstudianteId == estudiante.Id && x.ApoderadoId == apo.Id);
            if (!yaVinculado)
            {
                var tieneTitular = await _context.EstudiantesApoderados.AnyAsync(x => x.EstudianteId == estudiante.Id && x.EsSostenedorPrincipal);
                _context.EstudiantesApoderados.Add(new EstudianteApoderado
                {
                    EstudianteId = estudiante.Id, ApoderadoId = apo.Id,
                    Parentesco = string.IsNullOrWhiteSpace(parentesco) ? "Apoderado/a" : parentesco.Trim(),
                    EsSostenedorPrincipal = !tieneTitular
                });
            }
            await _context.SaveChangesAsync();
            var nHijos = await _context.EstudiantesApoderados.CountAsync(x => x.ApoderadoId == apo.Id);
            avisos.Add(apoNuevo
                ? $"Apoderado/a nuevo/a: {uApo.Nombre} {uApo.Apellido} (usuario {uApo.Email})."
                : $"El apoderado/a {uApo.Nombre} {uApo.Apellido} ya existía: se le sumó este alumno/a (ahora tiene {nHijos} {(nHijos == 1 ? "pupilo/a" : "pupilos/as")}).");

            // D) Acceso del apoderado nuevo: enlace por correo si tiene correo real; si no, clave provisoria en pantalla
            if (apoNuevo && EsCorreoReal(uApo.Email))
            {
                var r = await acceso.EnviarInvitacionAsync(uApo, $"{Request.Scheme}://{Request.Host}", User.FindFirst("ColegioNombre")?.Value ?? "Tu colegio");
                if (r.Enviado && !r.Simulado) { avisos.Add($"Se le envió a {uApo.Email} un enlace para crear su contraseña."); claveApo = null; }
            }

            var claves = new List<string>();
            if (claveAlumno != null) claves.Add($"Alumno/a: {uEst.Email} · clave {claveAlumno}");
            if (claveApo != null) claves.Add($"Apoderado/a: {uApo.Rut} o {uApo.Email} · clave {claveApo}");
            TempData["SuccessMessage"] = $"✅ Matrícula lista en {curso.Nombre} {curso.Letra}. " + string.Join(" ", avisos)
                + (claves.Count > 0 ? " Claves provisorias (anótalas, no se vuelven a mostrar; se cambian al primer ingreso): " + string.Join(" | ", claves) : "");
            return RedirectToAction("Alumnos", new { id = curso.Id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"No se pudo guardar la matrícula: {ex.InnerException?.Message ?? ex.Message}";
            return RedirectToAction(nameof(Matricular));
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
            var nuevo = await _context.Cursos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == nuevoCursoId);
            if (nuevo != null)
            {
                var hist = await _context.MatriculasAnuales.FirstOrDefaultAsync(m => m.EstudianteId == estudiante.Id && m.Anio == nuevo.AnioAcademico);
                if (hist == null) _context.MatriculasAnuales.Add(new MatriculaAnual { ColegioId = estudiante.ColegioId, EstudianteId = estudiante.Id, CursoId = nuevoCursoId, Anio = nuevo.AnioAcademico, Tipo = estudiante.TipoMatricula });
                else hist.CursoId = nuevoCursoId;
            }
            estudiante.FueCambiadoPorExcepcion = true;
            estudiante.MotivoCambioExcepcion = string.IsNullOrWhiteSpace(motivo) ? "Cambio por excepción directiva/UTP" : motivo;

            _context.Estudiantes.Update(estudiante);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "El estudiante fue cambiado de curso correctamente.";
        }

        return RedirectToAction("Alumnos", new { id = nuevoCursoId });
    }
}