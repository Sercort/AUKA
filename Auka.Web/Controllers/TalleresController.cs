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
            .Include(t => t.Sesiones)
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
        var ids = talleres.Select(t => t.Id).ToList();
        ViewBag.Logros = (await _context.LogrosTaller.AsNoTracking().Where(l => ids.Contains(l.TallerId)).ToListAsync())
            .GroupBy(l => l.TallerId).ToDictionary(g => g.Key, g => g.ToList());
        ViewBag.Interesados = await _context.InteresesTaller.CountAsync(i => !i.Atendido);
        // Alumno/a o apoderado/a: a quién puede anotar como interesado
        if (User.IsInRole("Estudiante"))
            ViewBag.MisAlumnos = await _context.Estudiantes.AsNoTracking().Where(e => e.UsuarioId == userId)
                .Select(e => new OpcionVm(e.Id, e.Usuario!.Nombre)).ToListAsync();
        else if (User.IsInRole("Apoderado"))
            ViewBag.MisAlumnos = await _context.EstudiantesApoderados.AsNoTracking()
                .Where(x => x.Apoderado != null && x.Apoderado.UsuarioId == userId && x.Estudiante != null)
                .Select(x => new OpcionVm(x.EstudianteId, x.Estudiante!.Usuario!.Nombre + " " + x.Estudiante.Usuario.Apellido)).ToListAsync();
        ViewBag.PuedeCrear = await Permisos.PuedeCrearClubesAsync(_context, User);
        return View(talleres);
    }

    // 2. Ficha del club: datos e integrantes
    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        var colegioId = ColegioId();
        var taller = await _context.Talleres.Include(t => t.Docente).Include(t => t.Sesiones)
            .FirstOrDefaultAsync(t => t.Id == id && t.ColegioId == colegioId);
        if (taller == null) return NotFound();
        ViewBag.PuedeEditarClub = await Permisos.PuedeCrearClubesAsync(_context, User);
        ViewBag.Lugares = await LugaresAsync();
        ViewBag.Logros = await _context.LogrosTaller.AsNoTracking().Where(l => l.TallerId == id).OrderByDescending(l => l.Fecha).ToListAsync();
        // Logros: los registra el profesor/a a cargo o quien crea clubes. Rendimiento: el profesor/a a cargo o la UTP.
        ViewBag.PuedeLogros = taller.DocenteId == UsuarioId() || ViewBag.PuedeEditarClub == true;
        ViewBag.PuedeRendimiento = taller.DocenteId == UsuarioId() || User.IsInRole("UTP");

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
        ViewBag.Promedios = ViewBag.PuedeRendimiento == true
            ? await Clubes.PromediosAsync(_context, integrantes.Select(i => i.EstudianteId).ToList())
            : new Dictionary<int, decimal>();
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
        var est = await _context.Estudiantes.Include(e => e.Curso).FirstOrDefaultAsync(e => e.Id == estudianteId && e.ColegioId == colegioId);
        if (est == null) return Json(new { ok = false, error = "Alumno/a no encontrado." });
        if (await _context.InscripcionesTaller.AnyAsync(i => i.TallerId == tallerId && i.EstudianteId == estudianteId)) return Json(new { ok = true, ya = true });
        var n = await _context.InscripcionesTaller.CountAsync(i => i.TallerId == tallerId);
        if (taller.Cupo is > 0 && n >= taller.Cupo) return Json(new { ok = false, error = $"El club ya completó su cupo ({taller.Cupo})." });
        // Niveles, temporada y choques con sus clases (mandan) o con otros clubes
        var sesiones = await _context.SesionesTaller.AsNoTracking().Where(x => x.TallerId == tallerId).ToListAsync();
        var motivo = await Clubes.PuedeInscribirAsync(_context, taller, sesiones, est);
        if (motivo != null) return Json(new { ok = false, error = motivo });

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
    public async Task<IActionResult> Crear(Taller taller, int[]? sesDia, string[]? sesIni, string[]? sesFin, string[]? niveles)
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

        // Días y horas del club: se revisa que no choque con otro club (lugar o profesor/a) ni con las clases del profesor/a
        var errSes = new List<string>();
        var sesiones = LeerSesiones(sesDia, sesIni, sesFin, errSes);
        taller.Niveles = string.Join(",", Clubes.LeerNiveles(string.Join(",", niveles ?? Array.Empty<string>())));
        if (string.IsNullOrEmpty(taller.Niveles)) taller.Niveles = null;
        taller.Lugar = string.IsNullOrWhiteSpace(taller.Lugar) ? null : taller.Lugar.Trim();
        if (taller.FechaInicio != null && taller.FechaFin != null && taller.FechaFin < taller.FechaInicio) errSes.Add("La fecha de término es antes del inicio.");
        if (ModelState.IsValid && docente != null && errSes.Count == 0)
            errSes.AddRange(await Clubes.ChoquesClubAsync(_context, colegioId, null, docente.Id, taller.Lugar, taller.FechaInicio, taller.FechaFin, sesiones));
        foreach (var e in errSes) ModelState.AddModelError(string.Empty, e);

        if (ModelState.IsValid && docente != null)
        {
            taller.ColegioId = colegioId;
            taller.DocenteCargo = $"{docente.Nombre} {docente.Apellido}";
            foreach (var se in sesiones) { se.ColegioId = colegioId; taller.Sesiones.Add(se); }
            if (sesiones.Count > 0) taller.Horario = Clubes.Texto(sesiones);
            _context.Talleres.Add(taller);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Club «{taller.Nombre}» creado. Ya puedes sumar integrantes.";
            return RedirectToAction(nameof(Detalle), new { id = taller.Id });
        }

        await CargarDocentesAsync(taller.DocenteId);
        return View(taller);
    }

    /// <summary>Lee las filas día/inicio/fin del formulario (las vacías se ignoran).</summary>
    private static List<SesionTaller> LeerSesiones(int[]? dia, string[]? ini, string[]? fin, List<string> errores)
    {
        var r = new List<SesionTaller>();
        for (var i = 0; i < (dia?.Length ?? 0); i++)
        {
            if (dia![i] < 1 || dia[i] > 6) continue;
            var t1 = ini != null && i < ini.Length ? ini[i] : null;
            var t2 = fin != null && i < fin.Length ? fin[i] : null;
            if (string.IsNullOrWhiteSpace(t1) && string.IsNullOrWhiteSpace(t2)) continue;
            if (!Clubes.TryHora(t1, out var h1) || !Clubes.TryHora(t2, out var h2)) { errores.Add($"{Clubes.Dias[dia[i] - 1]}: revisa la hora de inicio y de término."); continue; }
            r.Add(new SesionTaller { Dia = dia[i], HoraInicio = h1, HoraFin = h2 });
        }
        return r;
    }

    /// <summary>Aviso en la campana al profesor/a y a los integrantes cuando cambia el horario del club.</summary>
    private async Task AvisarCambioClubAsync(Taller t, string mensaje)
    {
        var usuarios = await _context.InscripcionesTaller.Where(i => i.TallerId == t.Id && i.Estudiante != null)
            .Select(i => i.Estudiante!.UsuarioId).ToListAsync();
        if (t.DocenteId != null) usuarios.Add(t.DocenteId.Value);
        foreach (var u in usuarios.Distinct().Where(u => u != UsuarioId()))
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = t.ColegioId, UsuarioId = u, Titulo = $"🌟 Cambio en el club «{t.Nombre}»", Mensaje = mensaje,
                Enlace = $"/Talleres/Detalle/{t.Id}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
        await _context.SaveChangesAsync();
    }

    // 8. Editar horario, lugar, niveles y temporada del club (quien puede crear clubes)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarHorarioClub(int id, string? lugar, string[]? niveles, DateOnly? fechaInicio, DateOnly? fechaFin, int? cupo, int? cupoMinimo,
        int[]? sesDia, string[]? sesIni, string[]? sesFin)
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User)) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var t = await _context.Talleres.Include(x => x.Sesiones).FirstOrDefaultAsync(x => x.Id == id && x.ColegioId == colegioId);
        if (t == null) return NotFound();

        var errores = new List<string>();
        var nuevas = LeerSesiones(sesDia, sesIni, sesFin, errores);
        lugar = string.IsNullOrWhiteSpace(lugar) ? null : lugar.Trim();
        if (fechaInicio != null && fechaFin != null && fechaFin < fechaInicio) errores.Add("La fecha de término es antes del inicio.");
        if (errores.Count == 0) errores.AddRange(await Clubes.ChoquesClubAsync(_context, colegioId, t.Id, t.DocenteId, lugar, fechaInicio, fechaFin, nuevas));

        // Las clases mandan: si con el nuevo horario algún integrante queda con choque, no se cambia
        if (errores.Count == 0)
        {
            var copia = new Taller { Id = t.Id, Nombre = t.Nombre, Niveles = string.Join(",", Clubes.LeerNiveles(string.Join(",", niveles ?? Array.Empty<string>()))), FechaInicio = fechaInicio, FechaFin = fechaFin };
            var integrantes = await _context.Estudiantes.Include(e => e.Curso).Include(e => e.Usuario)
                .Where(e => _context.InscripcionesTaller.Any(i => i.TallerId == t.Id && i.EstudianteId == e.Id)).ToListAsync();
            foreach (var e in integrantes)
            {
                var m = await Clubes.PuedeInscribirAsync(_context, copia, nuevas, e);
                if (m != null && !m.StartsWith("El club ya terminó")) errores.Add($"🙋 {e.Usuario?.Nombre} {e.Usuario?.Apellido} ({e.Curso?.Nombre} {e.Curso?.Letra}): {m}");
                if (errores.Count > 12) break;
            }
        }
        if (errores.Count > 0)
        {
            TempData["Error"] = "No se guardó el cambio: " + string.Join(" · ", errores.Take(12));
            return RedirectToAction(nameof(Detalle), new { id });
        }

        var antes = t.Horario;
        _context.SesionesTaller.RemoveRange(t.Sesiones);
        t.Sesiones.Clear();
        foreach (var se in nuevas) { se.ColegioId = colegioId; se.TallerId = t.Id; t.Sesiones.Add(se); }
        t.Horario = nuevas.Count > 0 ? Clubes.Texto(nuevas) : null;
        t.Lugar = lugar;
        t.Niveles = string.Join(",", Clubes.LeerNiveles(string.Join(",", niveles ?? Array.Empty<string>())));
        if (string.IsNullOrEmpty(t.Niveles)) t.Niveles = null;
        t.FechaInicio = fechaInicio; t.FechaFin = fechaFin;
        t.Cupo = cupo is > 0 ? cupo : null;
        t.CupoMinimo = cupoMinimo is > 0 ? cupoMinimo : null;
        await _context.SaveChangesAsync();
        if (antes != t.Horario)
            await AvisarCambioClubAsync(t, $"Nuevo horario: {t.Horario ?? "por definir"}{(t.Lugar != null ? " · " + t.Lugar : "")}.");
        TempData["SuccessMessage"] = "✅ Club actualizado." + (antes != t.Horario ? " Se avisó al profesor/a y a los integrantes." : "");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    // 9. Importar clubes desde Excel (CSV): una fila por día del club
    /// <summary>
    /// Columnas: Club; Categoría; RUT profesor; Día; Hora inicio; Hora fin; Lugar; Cupo; Niveles; Fecha inicio; Fecha fin; Descripción.
    /// Si el club ya existe (mismo nombre), se actualiza su horario con el de la planilla. Los clubes con choques no se cargan y se avisa por qué.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> ImportarClubes(IFormFile? archivo)
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User)) return RedirectToAction("AccesoDenegado", "Account");
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var pl = await Csv.LeerAsync(archivo);
        int iClub = pl.Col("club", "taller", "nombre"), iCat = pl.Col("categoria"), iRut = pl.Col("rut"), iDia = pl.Col("dia"),
            iIni = pl.Col("hora inicio", "inicio", "desde"), iFin = pl.Col("hora fin", "hora termino", "termino", "hasta"), iLug = pl.Col("lugar"),
            iCupo = pl.Col("cupo"), iMin = pl.Col("cupo minimo", "minimo"), iNiv = pl.Col("niveles", "nivel", "para"), iFI = pl.Col("fecha inicio"), iFF = pl.Col("fecha fin", "fecha termino"), iDes = pl.Col("descripcion");
        if (iClub < 0 || iDia < 0 || iIni < 0 || iFin < 0) { TempData["Error"] = "Faltan columnas. Usa la plantilla: Club, RUT profesor, Día, Hora inicio y Hora fin."; return RedirectToAction(nameof(Index)); }

        var colegioId = ColegioId();
        var docentes = (await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente).ToListAsync())
            .GroupBy(u => Rut.Normalizar(u.Rut)).ToDictionary(g => g.Key, g => g.First());
        var existentes = await _context.Talleres.Include(t => t.Sesiones).Where(t => t.ColegioId == colegioId && t.Activo).ToListAsync();
        var errores = new List<string>();
        int creados = 0, actualizados = 0;

        string C(string[] f, int k) => Csv.Planilla.Celda(f, k);
        var grupos = pl.Filas.Select((f, i) => (f, fila: i + 2)).Where(x => C(x.f, iClub).Length > 0).GroupBy(x => Csv.Clave(C(x.f, iClub)));
        foreach (var g in grupos)
        {
            var primera = g.First().f;
            var nombre = C(primera, iClub);
            var sesiones = new List<SesionTaller>();
            var malas = false;
            foreach (var (f, fila) in g)
            {
                var d = Clubes.DiaDesdeTexto(C(f, iDia));
                if (d == 0 || !Clubes.TryHora(C(f, iIni), out var h1) || !Clubes.TryHora(C(f, iFin), out var h2))
                { errores.Add($"Fila {fila} ({nombre}): revisa el día y las horas."); malas = true; continue; }
                sesiones.Add(new SesionTaller { Dia = d, HoraInicio = h1, HoraFin = h2, ColegioId = colegioId });
            }
            if (malas) continue;

            var t = existentes.FirstOrDefault(x => Csv.Clave(x.Nombre) == g.Key);
            Usuario? doc = null;
            if (Rut.TryValidar(C(primera, iRut), out var rutDoc, out _)) docentes.TryGetValue(rutDoc, out doc);
            if (t == null && doc == null) { errores.Add($"«{nombre}»: falta el RUT de un profesor/a del colegio (debe estar en Personal)."); continue; }
            var lugar = C(primera, iLug); lugar = lugar.Length == 0 ? (t?.Lugar ?? "") : lugar;
            var fi = iFI >= 0 ? Csv.Fecha(C(primera, iFI)) : null;
            var ff = iFF >= 0 ? Csv.Fecha(C(primera, iFF)) : null;
            DateOnly? fIni = fi != null ? DateOnly.FromDateTime(fi.Value) : t?.FechaInicio;
            DateOnly? fFin = ff != null ? DateOnly.FromDateTime(ff.Value) : t?.FechaFin;

            var choques = await Clubes.ChoquesClubAsync(_context, colegioId, t?.Id, doc?.Id ?? t?.DocenteId, lugar, fIni, fFin, sesiones);
            if (choques.Count > 0) { errores.Add($"«{nombre}» no se cargó: {string.Join(" ", choques.Take(3))}"); continue; }

            if (t == null)
            {
                t = new Taller { Nombre = nombre.Length > 120 ? nombre[..120] : nombre, ColegioId = colegioId, Activo = true };
                _context.Talleres.Add(t);
                existentes.Add(t);
                creados++;
            }
            else
            {
                _context.SesionesTaller.RemoveRange(t.Sesiones);
                t.Sesiones.Clear();
                actualizados++;
            }
            var cat = C(primera, iCat);
            var catOk = Categorias.FirstOrDefault(c => Csv.Clave(c.Clave) == Csv.Clave(cat)).Clave;
            if (catOk != null) t.Categoria = catOk;
            if (doc != null) { t.DocenteId = doc.Id; t.DocenteCargo = $"{doc.Nombre} {doc.Apellido}"; }
            t.Lugar = lugar.Length == 0 ? null : lugar;
            if (int.TryParse(C(primera, iCupo), out var cupo) && cupo > 0) t.Cupo = cupo;
            if (iMin >= 0 && int.TryParse(C(primera, iMin), out var cupoMin) && cupoMin > 0) t.CupoMinimo = cupoMin;
            if (iNiv >= 0 && C(primera, iNiv).Length > 0) { var nv = Clubes.NivelesDesdeTexto(C(primera, iNiv)); t.Niveles = nv.Count > 0 ? string.Join(",", nv) : null; }
            t.FechaInicio = fIni; t.FechaFin = fFin;
            var des = C(primera, iDes); if (des.Length > 0) t.Descripcion = des.Length > 500 ? des[..500] : des;
            foreach (var se in sesiones) t.Sesiones.Add(se);
            t.Horario = Clubes.Texto(sesiones);
            await _context.SaveChangesAsync();   // se guarda club por club: así el siguiente ya ve sus horarios
        }

        TempData["SuccessMessage"] = $"📥 Clubes: {creados} nuevos, {actualizados} actualizados.";
        if (errores.Count > 0) TempData["Error"] = $"{errores.Count} {(errores.Count == 1 ? "problema" : "problemas")}: " + string.Join(" · ", errores.Take(10)) + (errores.Count > 10 ? " …" : "");
        return RedirectToAction(nameof(Index));
    }

    // 10. Importar inscripciones: RUT alumno; Club
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> ImportarInscripciones(IFormFile? archivo)
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User)) return RedirectToAction("AccesoDenegado", "Account");
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var pl = await Csv.LeerAsync(archivo);
        int iRut = pl.Col("rut"), iClub = pl.Col("club", "taller");
        if (iRut < 0 || iClub < 0) { TempData["Error"] = "Faltan columnas. Usa la plantilla: RUT alumno y Club."; return RedirectToAction(nameof(Index)); }

        var colegioId = ColegioId();
        var anio = DateTime.Now.Year;
        var talleres = await _context.Talleres.Include(t => t.Sesiones).Where(t => t.ColegioId == colegioId && t.Activo).ToListAsync();
        var porNombre = talleres.GroupBy(t => Csv.Clave(t.Nombre)).ToDictionary(g => g.Key, g => g.First());
        var estudiantes = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null && e.Curso.AnioAcademico == anio).ToListAsync();
        var porRut = estudiantes.GroupBy(e => Rut.Normalizar(e.Usuario!.Rut)).ToDictionary(g => g.Key, g => g.First());
        var ya = (await _context.InscripcionesTaller.Where(i => i.ColegioId == colegioId).Select(i => new { i.TallerId, i.EstudianteId }).ToListAsync())
            .Select(x => (x.TallerId, x.EstudianteId)).ToHashSet();
        var conteo = talleres.ToDictionary(t => t.Id, t => ya.Count(x => x.TallerId == t.Id));
        var errores = new List<string>();
        int nuevas = 0, repetidas = 0;

        for (var i = 0; i < pl.Filas.Count; i++)
        {
            var f = pl.Filas[i]; var fila = i + 2;
            var rutTxt = Csv.Planilla.Celda(f, iRut); var club = Csv.Planilla.Celda(f, iClub);
            if (rutTxt.Length == 0 && club.Length == 0) continue;
            if (!Rut.TryValidar(rutTxt, out var rut, out _) || !porRut.TryGetValue(rut, out var est)) { errores.Add($"Fila {fila}: no hay alumno/a con RUT {rutTxt}."); continue; }
            if (!porNombre.TryGetValue(Csv.Clave(club), out var t)) { errores.Add($"Fila {fila}: no existe el club «{club}» (impórtalo primero)."); continue; }
            if (ya.Contains((t.Id, est.Id))) { repetidas++; continue; }
            if (t.Cupo is > 0 && conteo[t.Id] >= t.Cupo) { errores.Add($"Fila {fila}: «{t.Nombre}» completó su cupo."); continue; }
            var m = await Clubes.PuedeInscribirAsync(_context, t, t.Sesiones.ToList(), est);
            if (m != null) { errores.Add($"Fila {fila} ({est.Usuario!.Nombre} {est.Usuario.Apellido}, {t.Nombre}): {m}"); continue; }
            _context.InscripcionesTaller.Add(new InscripcionTaller { ColegioId = colegioId, TallerId = t.Id, EstudianteId = est.Id, FechaUtc = DateTime.UtcNow, InscritoPorId = UsuarioId() });
            await _context.SaveChangesAsync();   // uno a uno: así se detectan choques entre clubes de la misma planilla
            ya.Add((t.Id, est.Id)); conteo[t.Id]++; nuevas++;
        }
        TempData["SuccessMessage"] = $"📥 Inscripciones: {nuevas} nuevas" + (repetidas > 0 ? $", {repetidas} ya estaban" : "") + ".";
        if (errores.Count > 0) TempData["Error"] = $"{errores.Count} no se pudieron: " + string.Join(" · ", errores.Take(10)) + (errores.Count > 10 ? " …" : "");
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- Interesados
    /// <summary>Alumno/a o apoderado/a: "me interesa este club" (sin cupo, no es de su nivel o un club que aún no existe). Le llega a la UTP.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MeInteresa(int estudianteId, int? tallerId, string? categoria, string? comentario)
    {
        var yo = UsuarioId();
        var colegioId = ColegioId();
        // Solo por sí mismo/a o por sus pupilos
        var puede = await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId && e.UsuarioId == yo)
            || await _context.EstudiantesApoderados.AnyAsync(x => x.EstudianteId == estudianteId && x.Apoderado != null && x.Apoderado.UsuarioId == yo);
        if (!puede) return RedirectToAction("AccesoDenegado", "Account");
        if (tallerId != null && !await _context.Talleres.AnyAsync(t => t.Id == tallerId && t.ColegioId == colegioId)) tallerId = null;
        if (await _context.InteresesTaller.AnyAsync(i => i.EstudianteId == estudianteId && i.TallerId == tallerId && !i.Atendido && (tallerId != null || i.Categoria == categoria)))
        {
            TempData["SuccessMessage"] = "Ya estaba registrado tu interés. La UTP lo tiene en su lista.";
            return RedirectToAction(nameof(Index));
        }
        comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim()[..Math.Min(comentario.Trim().Length, 300)];
        _context.InteresesTaller.Add(new InteresTaller { ColegioId = colegioId, EstudianteId = estudianteId, TallerId = tallerId, Categoria = tallerId == null ? categoria : null, Comentario = comentario, RegistradoPorId = yo });
        var utp = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync();
        var nombre = await _context.Estudiantes.Where(e => e.Id == estudianteId).Select(e => e.Usuario!.Nombre + " " + e.Usuario.Apellido + " (" + e.Curso!.Nombre + " " + e.Curso.Letra + ")").FirstOrDefaultAsync();
        var club = tallerId != null ? await _context.Talleres.Where(t => t.Id == tallerId).Select(t => t.Nombre).FirstOrDefaultAsync() : $"un club nuevo ({categoria ?? "cualquier tipo"})";
        foreach (var u in utp)
            _context.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = u, Titulo = "🙋 Interesado/a en un club", Mensaje = $"{nombre} quiere {club}." + (comentario != null ? $" «{comentario}»" : ""), Enlace = "/Talleres/Interesados", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "🙋 ¡Listo! La UTP recibió tu interés y te avisará si se abre un cupo o un grupo nuevo.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>La UTP ve a los interesados agrupados por club o por tipo, y por tramo de curso (para abrir grupos nuevos).</summary>
    [HttpGet]
    public async Task<IActionResult> Interesados()
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User) && !User.IsInRole("UTP")) return RedirectToAction("AccesoDenegado", "Account");
        var colegioId = ColegioId();
        var lista = await _context.InteresesTaller.AsNoTracking().Where(i => !i.Atendido)
            .Select(i => new InteresadoVm(i.Id, i.EstudianteId, i.Estudiante!.Usuario!.Nombre + " " + i.Estudiante.Usuario.Apellido,
                i.Estudiante.Curso != null ? (i.Estudiante.Curso.Nombre + " " + i.Estudiante.Curso.Letra).Trim() : "", i.Estudiante.Curso != null ? i.Estudiante.Curso.Nombre : "",
                i.TallerId, i.Categoria, i.Comentario, i.CreadoUtc)).ToListAsync();
        ViewBag.Talleres = await _context.Talleres.AsNoTracking().Include(t => t.Inscripciones).Where(t => t.ColegioId == colegioId && t.Activo).ToListAsync();
        return View(lista);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtenderInteres(int[] ids)
    {
        if (!await Permisos.PuedeCrearClubesAsync(_context, User) && !User.IsInRole("UTP")) return RedirectToAction("AccesoDenegado", "Account");
        foreach (var i in await _context.InteresesTaller.Where(x => ids.Contains(x.Id)).ToListAsync()) i.Atendido = true;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Marcado como atendido.";
        return RedirectToAction(nameof(Interesados));
    }

    // ---------------------------------------------------------------- Rendimiento
    /// <summary>
    /// El profesor/a a cargo o la UTP sacan a un alumno/a del club por notas. Se avisa a la otra parte (UTP ↔ profesor/a).
    /// No se avisa a los compañeros/as ni al apoderado/a. Cuando suba su promedio, AUKA avisa que puede volver.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarPorRendimiento(int id)
    {
        var colegioId = ColegioId();
        var ins = await _context.InscripcionesTaller.Include(i => i.Taller).Include(i => i.Estudiante).ThenInclude(e => e!.Usuario)
            .FirstOrDefaultAsync(i => i.Id == id && i.ColegioId == colegioId);
        if (ins?.Taller == null) return NotFound();
        var esDocente = ins.Taller.DocenteId == UsuarioId();
        if (!esDocente && !User.IsInRole("UTP")) return RedirectToAction("AccesoDenegado", "Account");
        var prom = (await Clubes.PromediosAsync(_context, new List<int> { ins.EstudianteId })).GetValueOrDefault(ins.EstudianteId);
        _context.SalidasTaller.Add(new SalidaTaller { ColegioId = colegioId, TallerId = ins.TallerId, EstudianteId = ins.EstudianteId, Motivo = "Rendimiento", Promedio = prom > 0 ? prom : null, PorId = UsuarioId() });
        _context.InscripcionesTaller.Remove(ins);
        var nombre = $"{ins.Estudiante?.Usuario?.Nombre} {ins.Estudiante?.Usuario?.Apellido}";
        var destino = esDocente
            ? await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync()
            : (ins.Taller.DocenteId != null ? new List<int> { ins.Taller.DocenteId.Value } : new List<int>());
        foreach (var u in destino.Where(u => u != UsuarioId()))
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = colegioId, UsuarioId = u, Titulo = "📉 Salida de club por rendimiento",
                Mensaje = $"{User.Identity?.Name} sacó a {nombre} de «{ins.Taller.Nombre}» por rendimiento" + (prom > 0 ? $" (promedio {Csv.Nota1(prom)})" : "") + ". Cuando suba sus notas, AUKA avisará que puede volver.",
                Enlace = $"/Talleres/Detalle/{ins.TallerId}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"{nombre} salió del club por rendimiento. Se avisó a {(esDocente ? "la UTP" : "el profesor/a a cargo")}. Cuando suba su promedio a 4,0 o más, AUKA avisará que puede volver.";
        return RedirectToAction(nameof(Detalle), new { id = ins.TallerId });
    }

    // ---------------------------------------------------------------- Logros
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarLogro(int tallerId, DateOnly? fecha, string? evento, string? resultado, string? detalle)
    {
        var colegioId = ColegioId();
        var t = await _context.Talleres.FirstOrDefaultAsync(x => x.Id == tallerId && x.ColegioId == colegioId);
        if (t == null) return NotFound();
        if (t.DocenteId != UsuarioId() && !await Permisos.PuedeCrearClubesAsync(_context, User)) return RedirectToAction("AccesoDenegado", "Account");
        evento = (evento ?? "").Trim();
        if (evento.Length < 3) { TempData["Error"] = "Escribe el nombre del campeonato, muestra o actividad."; return RedirectToAction(nameof(Detalle), new { id = tallerId }); }
        var r = Clubes.Resultado(resultado);
        var logro = new LogroTaller
        {
            ColegioId = colegioId, TallerId = tallerId, Fecha = fecha ?? DateOnly.FromDateTime(DateTime.Today),
            Evento = evento.Length > 120 ? evento[..120] : evento, Resultado = r.Clave,
            Detalle = string.IsNullOrWhiteSpace(detalle) ? null : detalle.Trim()[..Math.Min(detalle.Trim().Length, 300)], RegistradoPorId = UsuarioId()
        };
        _context.LogrosTaller.Add(logro);
        await _context.SaveChangesAsync();
        // Ganaron algo: felicitaciones a los integrantes y al profesor/a
        if (r.Gana)
            await AvisarCambioClubAsync(t, $"{r.Icono} ¡Felicitaciones! {r.Nombre} en «{logro.Evento}». Qué alegría, sigan así: ustedes pueden.");
        TempData["SuccessMessage"] = r.Gana ? $"{r.Icono} ¡Logro registrado! Se felicitó a los integrantes." : "Registrado. ¡Para la próxima!";
        return RedirectToAction(nameof(Detalle), new { id = tallerId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarLogro(int id)
    {
        var colegioId = ColegioId();
        var l = await _context.LogrosTaller.FirstOrDefaultAsync(x => x.Id == id);
        if (l == null) return NotFound();
        var t = await _context.Talleres.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.TallerId && x.ColegioId == colegioId);
        if (t == null || (t.DocenteId != UsuarioId() && !await Permisos.PuedeCrearClubesAsync(_context, User))) return RedirectToAction("AccesoDenegado", "Account");
        _context.LogrosTaller.Remove(l);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Detalle), new { id = l.TallerId });
    }

    private async Task<List<string>> LugaresAsync()
    {
        var colegioId = ColegioId();
        return (await _context.Talleres.AsNoTracking().Where(t => t.ColegioId == colegioId && t.Lugar != null).Select(t => t.Lugar!).ToListAsync())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l).ToList();
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
        ViewBag.Lugares = await LugaresAsync();
    }
}

public record IntegranteVm(int Id, int EstudianteId, string Nombre, string Rut, string Curso, DateTime FechaUtc);

public record InteresadoVm(int Id, int EstudianteId, string Nombre, string Curso, string Nivel, int? TallerId, string? Categoria, string? Comentario, DateTime CreadoUtc);
