using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Auka.Web.Controllers;

[Authorize(Policy = "AccesoPsicosocial")]
public class PsychosocialController : Controller
{
    // El administrador del Plan Psicosocial solo gestiona alumnos/as: nunca ve fichas, bitácoras ni calendario clínico
    private static readonly HashSet<string> AccionesAdministrador = new() { "Alumnos", "ImportarAlumnos", "AgregarAlumnos", "RegenerarClavesAlumnos" };
    private bool EsAdministrador => User.FindFirst(ClaimTypes.Role)?.Value == "SuperAdmin";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (EsAdministrador)
        {
            var accion = context.ActionDescriptor.RouteValues["action"] ?? "";
            if (!AccionesAdministrador.Contains(accion))
            {
                context.Result = accion == "Index" ? RedirectToAction("Index", "Administracion") : StatusCode(403);
                return;
            }
        }
        base.OnActionExecuting(context);
    }

    private static readonly HashSet<string> TiposValidos = new()
    {
        "Citación", "Taller", "Consejo", "Coordinación", "Reunión", "Institucional", "Feriado"
    };

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public PsychosocialController(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // GET: /Psychosocial
    [HttpGet]
    public IActionResult Index()
    {
        var modelo = new PsychosocialDashboardViewModel
        {
            NombreProfesional = User.Identity?.Name ?? "Profesional"
        };

        return View(modelo);
    }

    // GET: /Psychosocial/Resumen -> números reales del colegio (vacío si el colegio es nuevo)
    [HttpGet]
    public async Task<IActionResult> Resumen()
    {
        var alumnos = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null)
            .Select(e => new { e.Curso!.Nombre, e.Curso.Letra })
            .ToListAsync();

        var porCurso = alumnos
            .GroupBy(a => string.IsNullOrEmpty(a.Letra) ? a.Nombre : a.Nombre + " " + a.Letra)
            .Select(g => new { c = g.Key, n = g.Count() })
            .ToList();

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        int dow = ((int)hoy.DayOfWeek + 6) % 7; // lunes = 0
        var lunes = hoy.AddDays(-dow);
        var viernes = lunes.AddDays(4);
        var semana = await _context.EventosCalendario.AsNoTracking()
            .Where(e => e.Fecha >= lunes && e.Fecha <= viernes && e.Tipo == "Citación")
            .Select(e => e.Realizada).ToListAsync();

        var sinConfirmar = await _context.EventosCalendario.AsNoTracking()
            .Where(e => e.Fecha >= hoy && e.Tipo == "Citación" && !e.Realizada)
            .OrderBy(e => e.Fecha).ThenBy(e => e.Hora).Take(5).ToListAsync();

        return Json(new
        {
            alumnos = alumnos.Count,
            cursos = porCurso.Count,
            porCurso,
            citasSemana = semana.Count,
            citasSinConfirmar = semana.Count(x => !x),
            proximasCitas = sinConfirmar.Select(e => new
            {
                f = e.Fecha.ToString("dd-MM", CultureInfo.InvariantCulture),
                h = e.Hora.HasValue ? e.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
                x = e.Titulo
            })
        });
    }

    // ---------------------------------------------------------------
    // ANOTACIONES DE CONDUCTA (el mismo libro que usan profesores e inspectores)
    // Las ven profesores/as y apoderados/as; no son confidenciales como la bitácora.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/Anotaciones?estudianteId=5
    [HttpGet]
    public async Task<IActionResult> Anotaciones(int estudianteId)
    {
        // Estudiantes tiene filtro multitenant: si el alumno/a es de otro colegio, no existe para esta sesión
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound();

        var lista = await _context.Anotaciones.AsNoTracking()
            .Include(a => a.Docente)
            .Where(a => a.EstudianteId == estudianteId)
            .OrderByDescending(a => a.Fecha)
            .Take(30)
            .ToListAsync();

        return Json(lista.Select(a => new
        {
            id = a.Id,
            t = (int)a.Tipo,
            d = a.Detalle,
            f = a.Fecha.ToString("o", CultureInfo.InvariantCulture),
            a = a.Docente != null ? $"{a.Docente.Nombre} {a.Docente.Apellido}".Trim() : ""
        }));
    }

    // POST: /Psychosocial/CrearAnotacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearAnotacion(int estudianteId, TipoAnotacion tipo, string? detalle)
    {
        detalle = (detalle ?? "").Trim();
        if (detalle.Length < 3) return BadRequest(new { error = "Escribe el detalle de la anotación." });
        if (detalle.Length > 500) return BadRequest(new { error = "El detalle es muy largo (máximo 500 caracteres)." });
        if (!Enum.IsDefined(tipo)) return BadRequest(new { error = "Tipo de anotación no válido." });
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound(new { error = "Alumno/a no encontrado." });

        _context.Anotaciones.Add(new Anotacion
        {
            EstudianteId = estudianteId,
            DocenteId = UsuarioActualId(),
            Tipo = tipo,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // ---------------------------------------------------------------
    // CALENDARIO COMPARTIDO (psicóloga, trabajador social, orientadora)
    // El filtro multitenant del DbContext limita todo al colegio de la sesión.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/EventosCalendario?desde=2026-10-01&hasta=2026-10-31
    [HttpGet]
    public async Task<IActionResult> EventosCalendario(string desde, string hasta)
    {
        if (!DateOnly.TryParse(desde, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
            !DateOnly.TryParse(hasta, CultureInfo.InvariantCulture, DateTimeStyles.None, out var h))
        {
            return BadRequest("Rango de fechas inválido.");
        }

        var lista = await _context.EventosCalendario
            .AsNoTracking()
            .Where(e => e.Fecha >= d && e.Fecha <= h)
            .OrderBy(e => e.Fecha).ThenBy(e => e.Hora)
            .ToListAsync();

        return Json(lista.Select(e => new
        {
            id = e.Id,
            f = e.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            h = e.Hora.HasValue ? e.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            t = e.Tipo,
            x = e.Titulo,
            r = e.Responsable,
            ok = e.Realizada
        }));
    }

    // POST: crea (id = 0) o edita una nota
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarEvento(int id, string fecha, string? hora, string tipo,
        string titulo, string responsable, bool realizada)
    {
        if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return BadRequest("Fecha inválida.");
        if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 200)
            return BadRequest("Título inválido.");
        if (!TiposValidos.Contains(tipo))
            return BadRequest("Tipo inválido.");

        TimeOnly? horaEvento = TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hh) ? hh : null;
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);

        EventoCalendario evento;
        if (id == 0)
        {
            evento = new EventoCalendario { CreadoPorId = usuarioId };
            _context.EventosCalendario.Add(evento);
        }
        else
        {
            var existente = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
            if (existente == null) return NotFound();
            evento = existente;
            evento.ModificadoPorId = usuarioId;
        }

        evento.Fecha = f;
        evento.Hora = horaEvento;
        evento.Tipo = tipo;
        evento.Titulo = titulo.Trim();
        evento.Responsable = string.IsNullOrWhiteSpace(responsable) ? "Equipo psicosocial" : responsable;
        evento.Realizada = realizada;

        await _context.SaveChangesAsync();
        return Json(new { id = evento.Id });
    }

    // POST: marca o desmarca una nota como realizada
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarEvento(int id, bool realizada)
    {
        var evento = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
        if (evento == null) return NotFound();

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        evento.Realizada = realizada;
        evento.ModificadoPorId = usuarioId;

        await _context.SaveChangesAsync();
        return Json(new { id = evento.Id });
    }

    // POST: elimina una nota (borrado lógico: el DbContext marca IsDeleted)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarEvento(int id)
    {
        var evento = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
        if (evento == null) return NotFound();

        _context.EventosCalendario.Remove(evento);
        await _context.SaveChangesAsync();
        return Json(new { id });
    }

    // ---------------------------------------------------------------
    // BENEFICIOS: sets pro-retención e inventario de ropa (compartidos por el equipo)
    // ---------------------------------------------------------------

    // GET: /Psychosocial/Beneficios
    [HttpGet]
    public async Task<IActionResult> Beneficios()
    {
        var sets = await _context.SetsBeneficio.AsNoTracking().ToListAsync();
        if (sets.Count == 0)
        {
            // Primera vez del colegio: se crean los dos sets que mencionó el equipo (editables)
            var anioNuevo = DateTime.Now.Year;
            _context.SetsBeneficio.AddRange(
                new SetBeneficio { Anio = anioNuevo, Nombre = "Set 1", Articulos = "Mochila\nPoleras\nParka\nZapatos\nÚtiles escolares", Orden = 1 },
                new SetBeneficio { Anio = anioNuevo, Nombre = "Set 2", Articulos = "Tablet", Orden = 2 });
            await _context.SaveChangesAsync();
            sets = await _context.SetsBeneficio.AsNoTracking().ToListAsync();
        }

        var anio = sets.Max(x => x.Anio);
        var inventario = await _context.ArticulosInventario.AsNoTracking().OrderBy(a => a.Nombre).ToListAsync();

        return Json(new
        {
            anio,
            sets = sets.Where(x => x.Anio == anio).OrderBy(x => x.Orden).Select(x => new
            {
                id = x.Id,
                n = x.Nombre,
                it = x.Articulos.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                t = x.CantidadAEntregar,
                e = x.Entregados
            }),
            inv = inventario.Select(a => new { id = a.Id, n = a.Nombre, ta = a.Tallas, s = a.Stock, m = a.Minimo })
        });
    }

    // POST: guarda los sets de un año (crea, edita y quita los que ya no están)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarSets(int anio, string setsJson)
    {
        List<SetDto>? lista;
        try { lista = JsonSerializer.Deserialize<List<SetDto>>(setsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return BadRequest("Datos inválidos."); }
        if (lista == null || anio < 2000 || anio > 2100) return BadRequest("Datos inválidos.");

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        var existentes = await _context.SetsBeneficio.Where(x => x.Anio == anio).ToListAsync();
        var conservados = new HashSet<SetBeneficio>();
        var orden = 1;

        foreach (var d in lista.Where(x => !string.IsNullOrWhiteSpace(x.n)))
        {
            var set = d.id > 0 ? existentes.FirstOrDefault(x => x.Id == d.id) : null;
            if (set == null)
            {
                set = new SetBeneficio { Anio = anio };
                _context.SetsBeneficio.Add(set);
            }
            else conservados.Add(set);

            set.Nombre = d.n.Trim();
            set.Articulos = string.Join("\n", (d.it ?? Array.Empty<string>()).Select(i => i.Trim()).Where(i => i.Length > 0));
            set.CantidadAEntregar = Math.Max(0, d.t);
            set.Entregados = Math.Max(0, d.e);
            set.Orden = orden++;
            set.ModificadoPorId = usuarioId;
        }

        foreach (var quitado in existentes.Where(x => !conservados.Contains(x)))
            _context.SetsBeneficio.Remove(quitado); // borrado lógico

        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: ingreso de stock (crea el artículo si no existe) y deja el movimiento registrado
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IngresarStock(string nombre, string? tallas, int cantidad)
    {
        if (string.IsNullOrWhiteSpace(nombre) || cantidad < 1 || cantidad > 100000) return BadRequest("Datos inválidos.");
        nombre = nombre.Trim();
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);

        var articulo = await _context.ArticulosInventario.FirstOrDefaultAsync(a => a.Nombre.ToLower() == nombre.ToLower());
        if (articulo == null)
        {
            articulo = new ArticuloInventario { Nombre = nombre, Tallas = tallas?.Trim() ?? "", Minimo = 3 };
            _context.ArticulosInventario.Add(articulo);
        }
        articulo.Stock += cantidad;
        articulo.ModificadoPorId = usuarioId;
        await _context.SaveChangesAsync();

        _context.MovimientosInventario.Add(new MovimientoInventario
        {
            ArticuloInventarioId = articulo.Id,
            Tipo = "Ingreso",
            Cantidad = cantidad,
            CreadoPorId = usuarioId
        });
        await _context.SaveChangesAsync();
        return Json(new { id = articulo.Id });
    }

    // POST: corrige el stock y el mínimo de un artículo (queda el movimiento "Ajuste")
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AjustarInventario(int id, int stock, int minimo)
    {
        var articulo = await _context.ArticulosInventario.FirstOrDefaultAsync(a => a.Id == id);
        if (articulo == null) return NotFound();

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        stock = Math.Max(0, stock);
        var diferencia = stock - articulo.Stock;
        articulo.Stock = stock;
        articulo.Minimo = Math.Max(0, minimo);
        articulo.ModificadoPorId = usuarioId;

        if (diferencia != 0)
        {
            _context.MovimientosInventario.Add(new MovimientoInventario
            {
                ArticuloInventarioId = articulo.Id,
                Tipo = "Ajuste",
                Cantidad = diferencia,
                Detalle = "Corrección manual",
                CreadoPorId = usuarioId
            });
        }

        await _context.SaveChangesAsync();
        return Json(new { id = articulo.Id });
    }
    // ---------------------------------------------------------------
    // ALUMNOS/AS: listado, alta manual e importación desde CSV
    // Cada alumno/a crea su cuenta (rol Estudiante) con clave provisoria y queda en su curso.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/Alumnos
    [HttpGet]
    public async Task<IActionResult> Alumnos()
    {
        var lista = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null)
            .ToListAsync();

        return Json(lista
            .OrderBy(e => e.Curso!.Nombre).ThenBy(e => e.Curso!.Letra).ThenBy(e => e.Usuario!.Apellido)
            .Select(e => new
            {
                id = e.Id,
                n = $"{e.Usuario!.Nombre} {e.Usuario.Apellido}".Trim(),
                r = e.Usuario.Rut,
                c = string.IsNullOrEmpty(e.Curso!.Letra) ? e.Curso.Nombre : $"{e.Curso.Nombre} {e.Curso.Letra}",
                f = e.FechaNacimiento.HasValue ? e.FechaNacimiento.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
            }));
    }

    // POST: importa un CSV (columnas: Nombre, RUT, Curso y, opcional, Fecha de nacimiento)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarAlumnos(IFormFile? archivo, bool crearCursos = true, bool actualizar = true)
    {
        if (archivo == null || archivo.Length == 0) return BadRequest(new { error = "Elige un archivo CSV." });
        if (archivo.Length > 2_000_000) return BadRequest(new { error = "El archivo es demasiado grande (máximo 2 MB)." });
        if (!archivo.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Por ahora solo se aceptan archivos CSV." });

        var lineas = new List<string>();
        using (var lector = new StreamReader(archivo.OpenReadStream(), Encoding.UTF8, true))
        {
            string? linea;
            while ((linea = await lector.ReadLineAsync()) != null)
                if (!string.IsNullOrWhiteSpace(linea)) lineas.Add(linea);
        }
        if (lineas.Count < 2) return BadRequest(new { error = "El archivo no tiene filas de alumnos/as." });
        if (lineas.Count > 2001) return BadRequest(new { error = "Máximo 2.000 alumnos/as por archivo." });

        var sep = lineas[0].Count(c => c == ';') >= lineas[0].Count(c => c == ',') ? ';' : ',';
        var cab = lineas[0].Split(sep).Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToList();
        int Col(params string[] claves) => cab.FindIndex(h => claves.Any(k => h.StartsWith(k)));
        int iNom = Col("nombre"), iRut = Col("rut"), iCur = Col("curso"), iNac = Col("fecha", "nacimiento");
        if (iNom < 0 || iRut < 0 || iCur < 0)
            return BadRequest(new { error = "Faltan columnas obligatorias: Nombre, RUT y Curso." });

        string Celda(string[] p, int i) => i >= 0 && i < p.Length ? p[i].Trim().Trim('"').Trim() : "";
        var filas = new List<FilaAlumno>();
        for (var i = 1; i < lineas.Count; i++)
        {
            var p = lineas[i].Split(sep);
            filas.Add(new FilaAlumno(i + 1, Celda(p, iNom), Celda(p, iRut), Celda(p, iCur), Celda(p, iNac)));
        }

        return Json(await ProcesarAlumnosAsync(filas, crearCursos, actualizar));
    }

    // POST: alta manual de uno o varios alumnos/as
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarAlumnos(string alumnosJson)
    {
        List<AlumnoDto>? lista;
        try { lista = JsonSerializer.Deserialize<List<AlumnoDto>>(alumnosJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return BadRequest(new { error = "Datos inválidos." }); }
        if (lista == null || lista.Count == 0 || lista.Count > 200)
            return BadRequest(new { error = "Agrega entre 1 y 200 alumnos/as." });

        var filas = lista.Select((a, i) => new FilaAlumno(i + 1, a.n?.Trim() ?? "", a.r?.Trim() ?? "", a.c?.Trim() ?? "", a.f)).ToList();
        return Json(await ProcesarAlumnosAsync(filas, true, true));
    }

    // ---------------------------------------------------------------
    // SEGUNDA CLAVE Y EXPEDIENTE CONFIDENCIAL DEL ALUMNO/A
    // Cada profesional crea su propia segunda clave la primera vez; al ingresarla se
    // desbloquean las fichas por 15 minutos. Cada apertura queda registrada.
    // ---------------------------------------------------------------

    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    private string LlaveDesbloqueo() => $"fichas-desbloqueadas:{UsuarioActualId()}";
    private bool FichasDesbloqueadas() => _cache.TryGetValue(LlaveDesbloqueo(), out _);
    private IActionResult Bloqueado() => StatusCode(403, new { bloqueada = true, error = "Vuelve a ingresar tu segunda clave." });

    private static bool CoincideClave(string email, string? hash, string clave)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try { return new PasswordHasher<string>().VerifyHashedPassword(email, hash, clave) != PasswordVerificationResult.Failed; }
        catch { return false; }
    }

    // GET: ¿tiene segunda clave? ¿está desbloqueada?
    [HttpGet]
    public async Task<IActionResult> EstadoClaveFicha()
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid);
        return Json(new { tieneClave = !string.IsNullOrEmpty(u?.ClaveFichaHash), desbloqueada = FichasDesbloqueadas() });
    }

    // POST: crea la segunda clave (solo si aún no tiene)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearClaveFicha(string clave, string confirmar)
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (!string.IsNullOrEmpty(u.ClaveFichaHash)) return BadRequest(new { error = "Ya tienes una segunda clave." });
        if (string.IsNullOrWhiteSpace(clave) || clave.Length < 6) return BadRequest(new { error = "La segunda clave debe tener al menos 6 caracteres." });
        if (clave != confirmar) return BadRequest(new { error = "Las claves no coinciden." });
        if (CoincideClave(u.Email, u.PasswordHash, clave)) return BadRequest(new { error = "Debe ser distinta de tu clave de ingreso." });

        u.ClaveFichaHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        await _context.SaveChangesAsync();
        _cache.Set(LlaveDesbloqueo(), true, TimeSpan.FromMinutes(15));
        return Json(new { ok = true });
    }

    // POST: desbloquea las fichas por 15 minutos (5 intentos fallidos bloquean 5 minutos)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DesbloquearFichas(string clave)
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (string.IsNullOrEmpty(u.ClaveFichaHash)) return BadRequest(new { error = "Primero crea tu segunda clave." });
        if (u.ClaveFichaBloqueadaHasta > DateTime.UtcNow) return StatusCode(429, new { error = "Demasiados intentos. Espera unos minutos e inténtalo de nuevo." });

        if (!CoincideClave(u.Email, u.ClaveFichaHash, clave ?? ""))
        {
            u.ClaveFichaIntentos++;
            if (u.ClaveFichaIntentos >= 5) { u.ClaveFichaBloqueadaHasta = DateTime.UtcNow.AddMinutes(5); u.ClaveFichaIntentos = 0; }
            await _context.SaveChangesAsync();
            return BadRequest(new { error = "Segunda clave incorrecta." });
        }

        u.ClaveFichaIntentos = 0;
        u.ClaveFichaBloqueadaHasta = null;
        await _context.SaveChangesAsync();
        _cache.Set(LlaveDesbloqueo(), true, TimeSpan.FromMinutes(15));
        return Json(new { ok = true });
    }

    // GET: /Psychosocial/Expediente/5  (exige fichas desbloqueadas y deja registro del acceso)
    [HttpGet]
    public async Task<IActionResult> Expediente(int id)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();

        var est = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (est?.Usuario == null || est.Curso == null) return NotFound();

        _context.AccesosExpediente.Add(new AccesoExpediente { EstudianteId = id, UsuarioId = UsuarioActualId() });
        await _context.SaveChangesAsync();

        var bitacora = await _context.BitacorasPsicosociales.AsNoTracking()
            .Where(b => b.EstudianteId == id).OrderByDescending(b => b.FechaAtencion).ToListAsync();
        var autoresIds = bitacora.Select(b => b.ProfesionalId).Distinct().ToList();
        var autores = await _context.Usuarios.AsNoTracking().Where(u => autoresIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.Nombre} {u.Apellido}".Trim());

        return Json(new
        {
            id = est.Id,
            n = $"{est.Usuario.Nombre} {est.Usuario.Apellido}".Trim(),
            r = est.Usuario.Rut,
            c = string.IsNullOrEmpty(est.Curso.Letra) ? est.Curso.Nombre : $"{est.Curso.Nombre} {est.Curso.Letra}",
            f = est.FechaNacimiento.HasValue ? est.FechaNacimiento.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
            email = est.Usuario.Email,
            bit = bitacora.Select(b => new
            {
                id = b.Id,
                t = b.Observaciones,
                f = b.FechaAtencion.ToString("o", CultureInfo.InvariantCulture),
                a = autores.GetValueOrDefault(b.ProfesionalId, "—"),
                m = b.FechaUltimaModificacion.HasValue
            })
        });
    }

    // POST: agrega una anotación a la bitácora confidencial
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarBitacora(int estudianteId, string observaciones)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        if (string.IsNullOrWhiteSpace(observaciones) || observaciones.Length > 4000) return BadRequest(new { error = "Escribe una anotación (máximo 4.000 caracteres)." });
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound();

        _context.BitacorasPsicosociales.Add(new BitacoraPsicosocial
        {
            EstudianteId = estudianteId,
            ProfesionalId = UsuarioActualId(),
            Observaciones = observaciones.Trim()
        });
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: corrige una anotación (la ve todo el equipo y queda marcada como editada)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarBitacora(int id, string observaciones)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        if (string.IsNullOrWhiteSpace(observaciones) || observaciones.Length > 4000) return BadRequest(new { error = "Escribe una anotación (máximo 4.000 caracteres)." });
        var b = await _context.BitacorasPsicosociales.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();

        b.Observaciones = observaciones.Trim();
        b.ModificadoPorId = UsuarioActualId();
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: genera una nueva clave provisoria de ingreso para el alumno/a (la anterior deja de servir)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestablecerClaveAlumno(int estudianteId)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        var est = await _context.Estudiantes.Include(e => e.Usuario).FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null) return NotFound();

        var clave = GenerarClave();
        est.Usuario.PasswordHash = new PasswordHasher<string>().HashPassword(est.Usuario.Email, clave);
        est.Usuario.DebeCambiarPassword = true;
        await _context.SaveChangesAsync();
        return Json(new { email = est.Usuario.Email, clave });
    }

    private async Task<object> ProcesarAlumnosAsync(List<FilaAlumno> filas, bool crearCursos, bool actualizar)
    {
        var errores = new List<string>();
        var credenciales = new List<object>();
        int creados = 0, actualizados = 0, omitidos = 0, cursosCreados = 0;
        var anio = DateTime.Now.Year;

        // El filtro multitenant limita todo esto al colegio de la sesión
        var cursos = await _context.Cursos.Where(c => c.AnioAcademico == anio).ToListAsync();
        var estudiantes = await _context.Estudiantes.Include(e => e.Usuario).ToListAsync();
        var porRut = estudiantes.Where(e => e.Usuario != null)
            .GroupBy(e => NormalizarRut(e.Usuario!.Rut)).ToDictionary(g => g.Key, g => g.First());
        var correos = (await _context.Usuarios.IgnoreQueryFilters()
            .Where(u => u.Email.EndsWith("@alumnos.auka.cl")).Select(u => u.Email).ToListAsync()).ToHashSet();

        var vistos = new HashSet<string>();
        var hasher = new PasswordHasher<string>();

        foreach (var f in filas)
        {
            if (string.IsNullOrWhiteSpace(f.Nombre)) { errores.Add($"Fila {f.Linea}: falta el nombre."); continue; }
            if (!TryNormalizarRut(f.Rut, out var rutNorm, out var rutFmt)) { errores.Add($"Fila {f.Linea}: RUT inválido ({f.Rut})."); continue; }
            if (!vistos.Add(rutNorm)) { errores.Add($"Fila {f.Linea}: RUT repetido en el archivo ({rutFmt})."); continue; }
            if (string.IsNullOrWhiteSpace(f.Curso)) { errores.Add($"Fila {f.Linea}: falta el curso."); continue; }
            var nac = ParseFecha(f.Nacimiento);
            if (!string.IsNullOrWhiteSpace(f.Nacimiento) && nac == null) { errores.Add($"Fila {f.Linea}: fecha de nacimiento inválida ({f.Nacimiento})."); continue; }

            var (nombreCurso, letra) = SepararCurso(f.Curso);
            var curso = cursos.FirstOrDefault(c =>
                string.Equals(c.Nombre.Trim(), nombreCurso, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((c.Letra ?? "").Trim(), letra, StringComparison.OrdinalIgnoreCase));
            if (curso == null)
            {
                if (!crearCursos) { errores.Add($"Fila {f.Linea}: el curso «{f.Curso}» no existe."); continue; }
                curso = new Curso { Nombre = nombreCurso, Letra = letra, AnioAcademico = anio };
                _context.Cursos.Add(curso);
                cursos.Add(curso);
                cursosCreados++;
            }

            if (porRut.TryGetValue(rutNorm, out var existente))
            {
                if (!actualizar) { omitidos++; continue; }
                existente.Curso = curso;
                if (nac != null) existente.FechaNacimiento = nac;
                actualizados++;
                continue;
            }

            var email = $"{rutNorm.ToLowerInvariant()}@alumnos.auka.cl";
            if (correos.Contains(email)) { errores.Add($"Fila {f.Linea}: el RUT {rutFmt} ya está registrado en otro colegio."); continue; }

            var (nombres, apellidos) = SepararNombre(f.Nombre);
            var clave = GenerarClave();
            var usuario = new Usuario
            {
                Rut = rutFmt,
                Nombre = nombres,
                Apellido = apellidos,
                Email = email,
                Rol = RolUsuario.Estudiante,
                Activo = true,
                DebeCambiarPassword = true,
                PasswordHash = hasher.HashPassword(email, clave)
            };
            _context.Estudiantes.Add(new Estudiante
            {
                Usuario = usuario,
                Curso = curso,
                FechaNacimiento = nac,
                TipoMatricula = TipoMatricula.Nuevo,
                FechaMatricula = DateTime.UtcNow
            });
            correos.Add(email);
            credenciales.Add(new { rut = rutFmt, n = f.Nombre, e = email, c = clave });
            creados++;
        }

        await _context.SaveChangesAsync();

        return new
        {
            creados,
            actualizados,
            omitidos,
            cursosCreados,
            errores = errores.Take(30),
            totalErrores = errores.Count,
            credenciales
        };
    }

    private static string NormalizarRut(string? rut) =>
        new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static bool TryNormalizarRut(string? entrada, out string norm, out string formateado)
    {
        norm = formateado = "";
        var limpio = NormalizarRut(entrada);
        if (limpio.Length < 7 || limpio.Length > 9) return false;
        var cuerpo = limpio[..^1];
        var dv = limpio[^1];
        if (!cuerpo.All(char.IsDigit)) return false;

        var suma = 0; var factor = 2;
        foreach (var d in cuerpo.Reverse()) { suma += (d - '0') * factor; factor = factor == 7 ? 2 : factor + 1; }
        var resto = 11 - suma % 11;
        var esperado = resto == 11 ? '0' : resto == 10 ? 'K' : (char)('0' + resto);
        if (dv != esperado) return false;

        var partes = new List<string>();
        for (var i = cuerpo.Length; i > 0; i -= 3) partes.Insert(0, cuerpo.Substring(Math.Max(0, i - 3), Math.Min(3, i)));
        norm = limpio;
        formateado = string.Join(".", partes) + "-" + dv;
        return true;
    }

    private static (string nombre, string letra) SepararCurso(string texto)
    {
        var partes = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length >= 2 && partes[^1].Length == 1 && char.IsLetter(partes[^1][0]))
            return (string.Join(" ", partes[..^1]), partes[^1].ToUpperInvariant());
        return (string.Join(" ", partes), "");
    }

    private static (string nombres, string apellidos) SepararNombre(string completo)
    {
        var p = completo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 3) return (string.Join(" ", p[..^2]), string.Join(" ", p[^2..]));
        if (p.Length == 2) return (p[0], p[1]);
        return (completo.Trim(), "");
    }

    private static string GenerarClave()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)]).ToArray());
    }

    private static DateOnly? ParseFecha(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        return DateOnly.TryParseExact(texto.Trim(), new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // ---------------------------------------------------------------
    // CLAVES DE INGRESO DE ALUMNOS/AS (las claves viejas no se pueden leer: están con hash)
    // ---------------------------------------------------------------

    // POST: nuevas claves provisorias solo para quienes aún no han cambiado la suya
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerarClavesAlumnos(string? curso)
    {
        if (!EsAdministrador && !FichasDesbloqueadas()) return Bloqueado();
        var lista = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Usuario.DebeCambiarPassword && e.Curso != null)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(curso))
            lista = lista.Where(e => (string.IsNullOrEmpty(e.Curso!.Letra) ? e.Curso.Nombre : $"{e.Curso.Nombre} {e.Curso.Letra}")
                .StartsWith(curso, StringComparison.OrdinalIgnoreCase)).ToList();

        var hasher = new PasswordHasher<string>();
        var credenciales = new List<object>();
        foreach (var e in lista)
        {
            var clave = GenerarClave();
            e.Usuario!.PasswordHash = hasher.HashPassword(e.Usuario.Email, clave);
            credenciales.Add(new { rut = e.Usuario.Rut, n = $"{e.Usuario.Nombre} {e.Usuario.Apellido}".Trim(), e = e.Usuario.Email, c = clave });
        }
        await _context.SaveChangesAsync();
        return Json(new { total = credenciales.Count, credenciales });
    }
}

public record SetDto(int id, string n, string[]? it, int t, int e);
public record FilaAlumno(int Linea, string Nombre, string Rut, string Curso, string? Nacimiento);
public record AlumnoDto(string? n, string? r, string? c, string? f);
