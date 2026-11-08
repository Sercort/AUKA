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

    // ---------------------------------------------------------------- Importar alumnos y apoderados (planilla)
    public record Credencial(string Curso, string Tipo, string Nombre, string Rut, string Usuario, string Clave);

    private static string SinTildes(string t)
    {
        var d = (t ?? "").Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Matrícula masiva desde CSV (Excel → Guardar como CSV). Columnas:
    /// Nivel; Letra; Especialidad; RUT alumno; Nombres alumno; Apellidos alumno; Correo alumno; Fecha nacimiento;
    /// RUT apoderado; Nombres apoderado; Apellidos apoderado; Correo apoderado; Teléfono apoderado; Parentesco.
    /// Crea cursos (año actual), alumnos/as, apoderados/as y los vincula (hermanos con el mismo apoderado/a).
    /// No envía correos: entrega una planilla con las claves provisorias para imprimir.
    /// </summary>
    // Una importación a la vez por colegio: si se presiona dos veces (o se recarga), la segunda no duplica cursos
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _importando = new();

    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> ImportarAlumnos(IFormFile? archivo)
    {
        var colLock = ColegioActual();
        if (!_importando.TryAdd(colLock, true))
        {
            TempData["Error"] = "Ya hay una importación en curso para este colegio. Espera a que termine (1 a 2 minutos) antes de volver a importar.";
            return RedirectToAction(nameof(Index));
        }
        try { return await ImportarAlumnosCore(archivo); }
        finally { _importando.TryRemove(colLock, out _); }
    }

    private async Task<IActionResult> ImportarAlumnosCore(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        if (!archivo.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) { TempData["Error"] = "Por ahora se lee CSV. En Excel: Archivo › Guardar como › CSV (delimitado por punto y coma)."; return RedirectToAction(nameof(Index)); }
        if (archivo.Length > 5_000_000) { TempData["Error"] = "El archivo es demasiado grande (máximo 5 MB)."; return RedirectToAction(nameof(Index)); }

        var lineas = new List<string>();
        using (var lector = new StreamReader(archivo.OpenReadStream(), System.Text.Encoding.UTF8, true))
        {
            string? l;
            while ((l = await lector.ReadLineAsync()) != null) if (!string.IsNullOrWhiteSpace(l)) lineas.Add(l);
        }
        if (lineas.Count < 2) { TempData["Error"] = "El archivo no tiene filas."; return RedirectToAction(nameof(Index)); }
        if (lineas.Count > 1501) { TempData["Error"] = "Máximo 1.500 alumnos/as por archivo."; return RedirectToAction(nameof(Index)); }

        var sep = lineas[0].Count(c => c == ';') >= lineas[0].Count(c => c == ',') ? ';' : ',';
        var cab = lineas[0].Split(sep).Select(h => SinTildes(h.Trim().Trim('"').TrimStart('\ufeff'))).ToList();
        int Col(params string[] k) => cab.FindIndex(h => k.Any(x => h.StartsWith(x)));
        int iNiv = Col("nivel", "curso"), iLet = Col("letra"), iEsp = Col("especialidad"),
            iRut = Col("rut alumno", "rut estudiante"), iNom = Col("nombres alumno", "nombre alumno", "nombres estudiante"), iApe = Col("apellidos alumno", "apellido alumno", "apellidos estudiante"),
            iCor = Col("correo alumno", "correo estudiante"), iNac = Col("fecha nac", "nacimiento"),
            iRutA = Col("rut apoderado"), iNomA = Col("nombres apoderado", "nombre apoderado"), iApeA = Col("apellidos apoderado", "apellido apoderado"),
            iCorA = Col("correo apoderado"), iTelA = Col("telefono apoderado", "telefono"), iPar = Col("parentesco"),
            iTipo = Col("tipo"), iOri = Col("colegio origen", "colegio de origen"), iRbd = Col("rbd"), iCom = Col("comuna"),
            iMot = Col("motivo"), iDoc = Col("documentos pendientes", "documentos"), iPie = Col("pie", "nee");
        if (iNiv < 0 || iRut < 0 || iNom < 0 || iRutA < 0)
        {
            TempData["Error"] = "Faltan columnas. Usa la plantilla: necesito al menos Nivel, RUT alumno, Nombres alumno y RUT apoderado.";
            return RedirectToAction(nameof(Index));
        }
        string C(string[] f, int i) => i >= 0 && i < f.Length ? f[i].Trim().Trim('"').Trim() : "";

        var colegioId = ColegioActual();
        var anio = DateTime.Now.Year;
        var hasher = new PasswordHasher<string>();
        var errores = new List<string>();
        var credenciales = new List<Credencial>();
        int alumnosNuevos = 0, alumnosExistentes = 0, apoNuevos = 0, cursosNuevos = 0, vinculos = 0;

        // Datos actuales del colegio (para no duplicar)
        var cursos = await _context.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync();
        var usuarios = await _context.Usuarios.Where(u => u.ColegioId == colegioId && (u.Rol == RolUsuario.Estudiante || u.Rol == RolUsuario.Apoderado)).ToListAsync();
        var porRut = usuarios.GroupBy(u => (Rut.Normalizar(u.Rut), u.Rol)).ToDictionary(g => g.Key, g => g.First());
        var correos = (await _context.Usuarios.IgnoreQueryFilters().Select(u => u.Email.ToLower()).ToListAsync()).ToHashSet();
        var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        var especialidades = (col?.Especialidades ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        for (int i = 1; i < lineas.Count; i++)
        {
            var f = lineas[i].Split(sep);
            var fila = i + 1;
            var nivelTxt = C(f, iNiv);
            if (nivelTxt.Length == 0 && C(f, iRut).Length == 0) continue;
            if (!Rut.TryValidar(C(f, iRut), out var rutEst, out var rutEstFmt)) { errores.Add($"Fila {fila}: RUT del alumno/a no válido ({C(f, iRut)})."); continue; }
            if (!Rut.TryValidar(C(f, iRutA), out var rutApo, out var rutApoFmt)) { errores.Add($"Fila {fila}: RUT del apoderado/a no válido ({C(f, iRutA)})."); continue; }
            if (rutEst == rutApo) { errores.Add($"Fila {fila}: el alumno/a y el apoderado/a tienen el mismo RUT."); continue; }
            var nom = C(f, iNom); var ape = C(f, iApe);
            if (nom.Length == 0) { errores.Add($"Fila {fila}: falta el nombre del alumno/a."); continue; }

            try
            {
                // Curso (nivel + letra) del año actual; se crea si no existe
                var orden = OrdenNivel(nivelTxt);
                var nivel = orden >= 0 ? NivelesOficiales[orden] : nivelTxt;
                var letra = C(f, iLet).ToUpperInvariant();
                if (letra.Length == 0) letra = "A";
                var curso = cursos.FirstOrDefault(c => Normal(c.Nombre) == Normal(nivel) && string.Equals(c.Letra, letra, StringComparison.OrdinalIgnoreCase));
                if (curso == null)
                {
                    curso = new Curso { Nombre = nivel, Letra = letra, CapacidadMaxima = 40, ColegioId = colegioId, AnioAcademico = anio };
                    curso.ProfesorJefeId = await JefeQueSigueAsync(colegioId, nivel, letra, anio);
                    _context.Cursos.Add(curso);
                    await _context.SaveChangesAsync();
                    cursos.Add(curso); cursosNuevos++;
                }
                var nombreCurso = $"{curso.Nombre} {curso.Letra}".Trim();
                var accesoPropio = !Citaciones.EsCursoMenor(curso.Nombre);

                // Alumno/a
                if (!porRut.TryGetValue((rutEst, RolUsuario.Estudiante), out var uEst))
                {
                    var correo = C(f, iCor).ToLowerInvariant();
                    if (!EsCorreoReal(correo) || correos.Contains(correo)) correo = $"{rutEst.ToLowerInvariant()}@alumnos.auka.cl";
                    var clave = GenerarClave();
                    uEst = new Usuario { Rut = rutEstFmt, Nombre = nom, Apellido = ape, Email = correo, Rol = RolUsuario.Estudiante, ColegioId = colegioId, Activo = true, DebeCambiarPassword = true, FechaCreacion = DateTime.UtcNow };
                    uEst.PasswordHash = hasher.HashPassword(correo, clave);
                    _context.Usuarios.Add(uEst);
                    await _context.SaveChangesAsync();
                    porRut[(rutEst, RolUsuario.Estudiante)] = uEst; correos.Add(correo); alumnosNuevos++;
                    if (accesoPropio) credenciales.Add(new Credencial(nombreCurso, "Alumno/a", $"{nom} {ape}", rutEstFmt, correo.EndsWith("auka.cl") ? rutEstFmt : correo, clave));
                }
                else alumnosExistentes++;

                var est = await _context.Estudiantes.FirstOrDefaultAsync(e => e.UsuarioId == uEst.Id);
                if (est == null)
                {
                    est = new Estudiante { UsuarioId = uEst.Id, ColegioId = colegioId, CursoId = curso.Id, TipoMatricula = TipoMatricula.Nuevo, FechaMatricula = DateTime.UtcNow, AccesoPropio = accesoPropio };
                    _context.Estudiantes.Add(est);
                }
                else { est.CursoId = curso.Id; est.AccesoPropio = accesoPropio; }
                if (DateOnly.TryParse(C(f, iNac), new System.Globalization.CultureInfo("es-CL"), out var nac)) est.FechaNacimiento = nac;
                // Tipo de matrícula y traslado (columnas opcionales)
                var tipoTxt = SinTildes(C(f, iTipo));
                if (tipoTxt.StartsWith("trasl")) est.TipoMatricula = TipoMatricula.Traslado;
                else if (tipoTxt.StartsWith("renov") || tipoTxt.StartsWith("antig")) est.TipoMatricula = TipoMatricula.Renovacion;
                else if (tipoTxt.StartsWith("nuev")) est.TipoMatricula = TipoMatricula.Nuevo;
                if (est.TipoMatricula == TipoMatricula.Traslado)
                {
                    est.ColegioOrigen = Limpio(C(f, iOri), 160);
                    est.RbdOrigen = Limpio(C(f, iRbd), 20);
                    est.ComunaOrigen = Limpio(C(f, iCom), 80);
                    est.MotivoTraslado = Limpio(C(f, iMot), 300);
                    est.FechaTraslado ??= DateOnly.FromDateTime(DateTime.Now);
                }
                var docs = C(f, iDoc).Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (docs.Length > 0) est.DocumentosPendientes = string.Join("|", docs);
                var pie = SinTildes(C(f, iPie));
                if (pie.StartsWith("si") || pie == "x" || pie == "1") est.NecesidadesEducativas = true;
                var esp = C(f, iEsp);
                if (EsMedioTP(curso.Nombre) && esp.Length > 0 && !SinTildes(esp).StartsWith("cientifico") && !SinTildes(esp).StartsWith("humanista"))
                {
                    est.Especialidad = esp;
                    if (!especialidades.Any(x => string.Equals(x, esp, StringComparison.OrdinalIgnoreCase))) especialidades.Add(esp);
                }
                await _context.SaveChangesAsync();
                var hist = await _context.MatriculasAnuales.FirstOrDefaultAsync(m => m.EstudianteId == est.Id && m.Anio == anio);
                if (hist == null) _context.MatriculasAnuales.Add(new MatriculaAnual { ColegioId = colegioId, EstudianteId = est.Id, CursoId = curso.Id, Anio = anio, Tipo = est.TipoMatricula });
                else hist.CursoId = curso.Id;

                // Apoderado/a (si ya existe, se le suma este hijo/a)
                if (!porRut.TryGetValue((rutApo, RolUsuario.Apoderado), out var uApo))
                {
                    var correo = C(f, iCorA).ToLowerInvariant();
                    if (!EsCorreoReal(correo) || correos.Contains(correo)) correo = $"{rutApo.ToLowerInvariant()}@apoderados.auka.cl";
                    var clave = GenerarClave();
                    uApo = new Usuario
                    {
                        Rut = rutApoFmt, Nombre = C(f, iNomA).Length > 0 ? C(f, iNomA) : "Apoderado/a", Apellido = C(f, iApeA), Email = correo,
                        Telefono = C(f, iTelA), Rol = RolUsuario.Apoderado, ColegioId = colegioId, Activo = true, DebeCambiarPassword = true, FechaCreacion = DateTime.UtcNow
                    };
                    uApo.PasswordHash = hasher.HashPassword(correo, clave);
                    _context.Usuarios.Add(uApo);
                    await _context.SaveChangesAsync();
                    porRut[(rutApo, RolUsuario.Apoderado)] = uApo; correos.Add(correo); apoNuevos++;
                    credenciales.Add(new Credencial(nombreCurso, "Apoderado/a", $"{uApo.Nombre} {uApo.Apellido}", rutApoFmt, correo.EndsWith("auka.cl") ? rutApoFmt : correo, clave));
                }
                var apo = await _context.Apoderados.FirstOrDefaultAsync(a => a.UsuarioId == uApo.Id);
                if (apo == null) { apo = new Apoderado { UsuarioId = uApo.Id, TelefonoContacto = C(f, iTelA) }; _context.Apoderados.Add(apo); await _context.SaveChangesAsync(); }
                if (!await _context.EstudiantesApoderados.AnyAsync(x => x.EstudianteId == est.Id && x.ApoderadoId == apo.Id))
                {
                    var tieneTitular = await _context.EstudiantesApoderados.AnyAsync(x => x.EstudianteId == est.Id && x.EsSostenedorPrincipal);
                    _context.EstudiantesApoderados.Add(new EstudianteApoderado { EstudianteId = est.Id, ApoderadoId = apo.Id, Parentesco = C(f, iPar).Length > 0 ? C(f, iPar) : "Apoderado/a", EsSostenedorPrincipal = !tieneTitular });
                    vinculos++;
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                errores.Add($"Fila {fila}: no se pudo guardar ({ex.GetBaseException().Message}).");
                _context.ChangeTracker.Clear();
                cursos = await _context.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync();
            }
        }

        if (col != null && especialidades.Count > 0)
        {
            var colegio = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
            if (colegio != null) { colegio.Especialidades = string.Join("|", especialidades.Distinct()); await _context.SaveChangesAsync(); }
        }

        ViewBag.Resumen = $"{alumnosNuevos} alumnos/as nuevos, {alumnosExistentes} ya existían, {apoNuevos} apoderados/as nuevos, {vinculos} vínculos alumno–apoderado, {cursosNuevos} cursos creados.";
        ViewBag.Errores = errores;
        ViewBag.Titulo = "Importación de alumnos/as y apoderados/as";
        return View("Claves", credenciales);
    }

    /// <summary>
    /// Claves provisorias de todo un curso, para imprimir o bajar en planilla.
    /// Por defecto solo para quienes aún no entran (no han cambiado su clave); así no se le cambia la clave a quien ya la usa.
    /// </summary>
    [Authorize(Roles = "UTP,Director,SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClavesCurso(int cursoId, bool apoderados = true, bool todos = false)
    {
        var colegioId = ColegioActual();
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId && c.ColegioId == colegioId);
        if (curso == null) return NotFound();
        var nombreCurso = $"{curso.Nombre} {curso.Letra}".Trim();
        var hasher = new PasswordHasher<string>();
        var lista = new List<Credencial>();

        var ests = await _context.Estudiantes.Include(e => e.Usuario)
            .Include(e => e.Apoderados).ThenInclude(a => a.Apoderado).ThenInclude(a => a!.Usuario)
            .Where(e => e.CursoId == cursoId && e.Usuario != null).AsSplitQuery().ToListAsync();
        var hechos = new HashSet<int>();
        foreach (var e in ests.OrderBy(x => x.Usuario!.Apellido))
        {
            var objetivos = new List<(Usuario U, string Tipo)>();
            if (e.AccesoPropio) objetivos.Add((e.Usuario!, "Alumno/a"));
            if (apoderados) objetivos.AddRange(e.Apoderados.Where(a => a.Apoderado?.Usuario != null).Select(a => (a.Apoderado!.Usuario!, "Apoderado/a de " + e.Usuario!.Nombre)));
            foreach (var (u, tipo) in objetivos)
            {
                if (!hechos.Add(u.Id)) continue;
                if (!todos && !u.DebeCambiarPassword) continue;   // ya entró y eligió su clave: no se toca
                var clave = GenerarClave();
                u.PasswordHash = hasher.HashPassword(u.Email, clave);
                u.DebeCambiarPassword = true;
                lista.Add(new Credencial(nombreCurso, tipo, $"{u.Nombre} {u.Apellido}", u.Rut, u.Email.EndsWith("auka.cl") ? u.Rut : u.Email, clave));
            }
        }
        await _context.SaveChangesAsync();
        ViewBag.Resumen = lista.Count == 0
            ? "Todos ya entraron y cambiaron su clave: no se generaron claves nuevas. (Marca «también a quienes ya entraron» para regenerarlas.)"
            : $"Se generaron {lista.Count} claves provisorias para {nombreCurso}. Las claves anteriores de estas personas dejan de funcionar.";
        ViewBag.Errores = new List<string>();
        ViewBag.Titulo = $"Claves provisorias · {nombreCurso}";
        ViewBag.CursoId = cursoId;
        return View("Claves", lista);
    }

    // ---------------------------------------------------------------- Períodos de matrícula
    /// <summary>La UTP, el director/a o el administrador abren y cierran la matrícula de cada año y eligen quién matricula.</summary>
    [Authorize(Roles = "SuperAdmin,Director,UTP")]
    [HttpGet]
    public async Task<IActionResult> PeriodosMatricula()
    {
        var periodos = await Matriculas.CargarAsync(_context, User);
        var colegioId = ColegioActual();
        var anios = periodos.Select(p => p.Anio).ToList();
        // Cuántos alumnos/as van matriculados en cada año con período (y cupos totales de sus cursos)
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && anios.Contains(c.AnioAcademico) && c.Nombre != "Pacientes")
            .Select(c => new { c.AnioAcademico, c.CapacidadMaxima, Alumnos = c.Estudiantes.Count }).ToListAsync();
        ViewBag.RotarJefes = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == colegioId).Select(c => c.RotarLetraJefes).FirstOrDefaultAsync();
        ViewBag.Avance = cursos.GroupBy(c => c.AnioAcademico).ToDictionary(g => g.Key, g => (Matriculados: g.Sum(x => x.Alumnos), Cupos: g.Sum(x => x.CapacidadMaxima)));
        return View(periodos);
    }

    [Authorize(Roles = "SuperAdmin,Director,UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AbrirMatricula(int anio, DateOnly? desde, DateOnly? hasta, string[]? roles)
    {
        var actual = DateTime.Now.Year;
        if (anio < actual || anio > actual + 2) { TempData["Error"] = "Elige el año actual o uno de los dos siguientes."; return RedirectToAction(nameof(PeriodosMatricula)); }
        var d = desde ?? Matriculas.Hoy;
        if (hasta != null && hasta < d) { TempData["Error"] = "La fecha de término no puede ser antes del inicio."; return RedirectToAction(nameof(PeriodosMatricula)); }
        var elegidos = (roles ?? Array.Empty<string>()).Where(r => Matriculas.Roles.Any(x => x.Clave == r)).Distinct().ToList();

        var colegioId = ColegioActual();
        var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        if (col == null) return NotFound();
        var lista = Matriculas.Leer(col.PeriodosMatricula);
        var p = lista.FirstOrDefault(x => x.Anio == anio);
        var nuevo = p == null;
        if (p == null) { p = new Matriculas.Periodo { Anio = anio }; lista.Add(p); }
        var antes = p.Roles.ToList();
        p.Desde = d; p.Hasta = hasta; p.Roles = elegidos; p.Cerrado = false;
        p.CambioPor = User.Identity?.Name; p.CambioUtc = DateTime.UtcNow;
        col.PeriodosMatricula = Matriculas.Escribir(lista);
        await _context.SaveChangesAsync();

        // Aviso en la campana a quienes ahora pueden matricular (solo si el período ya está abierto)
        var avisados = 0;
        if (Matriculas.Abierto(p)) avisados = await AvisarMatriculaAsync(colegioId, p, nuevo ? new List<string>() : antes);

        TempData["SuccessMessage"] = $"📝 Matrícula {anio} {(Matriculas.Estado(p) == "programada" ? "programada desde el " + d.ToString("dd-MM-yyyy") : "abierta")}" +
            (hasta != null ? $" hasta el {hasta:dd-MM-yyyy}" : "") + "." + (avisados > 0 ? $" Se avisó a {avisados} {(avisados == 1 ? "persona" : "personas")}." : "");
        return RedirectToAction(nameof(PeriodosMatricula));
    }

    [Authorize(Roles = "SuperAdmin,Director,UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarMatricula(int anio, bool reabrir = false)
    {
        var colegioId = ColegioActual();
        var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        if (col == null) return NotFound();
        var lista = Matriculas.Leer(col.PeriodosMatricula);
        var p = lista.FirstOrDefault(x => x.Anio == anio);
        if (p == null) return RedirectToAction(nameof(PeriodosMatricula));
        p.Cerrado = !reabrir;
        if (reabrir)
        {
            if (p.Hasta != null && p.Hasta < Matriculas.Hoy) p.Hasta = null;   // reabrir sin fecha de término
            if (p.Desde > Matriculas.Hoy) p.Desde = Matriculas.Hoy;
        }
        p.CambioPor = User.Identity?.Name; p.CambioUtc = DateTime.UtcNow;
        col.PeriodosMatricula = Matriculas.Escribir(lista);
        await _context.SaveChangesAsync();
        if (reabrir) await AvisarMatriculaAsync(colegioId, p, new List<string>());
        TempData["SuccessMessage"] = reabrir ? $"📝 Matrícula {anio} abierta de nuevo." : $"🔒 Matrícula {anio} cerrada. Ya no aparece para matricular (el administrador sí puede).";
        return RedirectToAction(nameof(PeriodosMatricula));
    }

    /// <summary>Notificación en la campana para quienes pueden matricular en el período (sin repetir a quienes ya podían).</summary>
    private async Task<int> AvisarMatriculaAsync(int colegioId, Matriculas.Periodo p, List<string> yaAvisados)
    {
        var nuevos = p.Roles.Except(yaAvisados).ToList();
        if (nuevos.Count == 0) return 0;
        var actual = DateTime.Now.Year;
        var jefes = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.ProfesorJefeId != null && (c.AnioAcademico == actual || c.AnioAcademico == p.Anio))
            .Select(c => c.ProfesorJefeId!.Value).Distinct().ToListAsync();
        var personas = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo &&
                ((nuevos.Contains("UTP") && u.Rol == RolUsuario.UTP) ||
                 (nuevos.Contains("Director") && u.Rol == RolUsuario.Director) ||
                 (nuevos.Contains("Orientadora") && u.Rol == RolUsuario.Orientadora) ||
                 (nuevos.Contains("Docente") && u.Rol == RolUsuario.Docente) ||
                 (nuevos.Contains("ProfesorJefe") && jefes.Contains(u.Id)) ||
                 (nuevos.Contains("InspectorGeneral") && u.Rol == RolUsuario.Inspector && u.EsInspectorGeneral)))
            .Select(u => u.Id).Distinct().ToListAsync();
        foreach (var id in personas)
            _context.Notificaciones.Add(new Notificacion
            {
                ColegioId = colegioId, UsuarioId = id, Titulo = $"📝 Matrícula {p.Anio} abierta",
                Mensaje = $"Ya puedes matricular alumnos/as para el año escolar {p.Anio}" + (p.Hasta != null ? $" hasta el {p.Hasta:dd-MM-yyyy}." : "."),
                Enlace = "/Cursos/Matricular", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo
            });
        await _context.SaveChangesAsync();
        return personas.Count;
    }

    /// <summary>
    /// Corrige duplicados del año actual: cursos repetidos (mismo nivel y letra) se juntan en uno,
    /// y alumnos/as o apoderados/as repetidos (mismo RUT) se dejan en una sola cuenta.
    /// </summary>
    [Authorize(Roles = "SuperAdmin,Director,UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorregirDuplicados()
    {
        var colegioId = ColegioActual();
        var anio = DateTime.Now.Year;
        int cursosJuntados = 0, alumnosJuntados = 0, apoJuntados = 0;

        // 1) Cursos repetidos: se queda el más antiguo; se le pasan alumnos/as, historial y horario
        var cursos = await _context.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync();
        foreach (var g in cursos.GroupBy(c => (Normal(c.Nombre), (c.Letra ?? "").Trim().ToUpperInvariant())).Where(g => g.Count() > 1))
        {
            var queda = g.OrderBy(c => c.Id).First();
            foreach (var dup in g.Where(c => c.Id != queda.Id))
            {
                await _context.Estudiantes.Where(e => e.CursoId == dup.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.CursoId, queda.Id));
                await _context.MatriculasAnuales.Where(m => m.CursoId == dup.Id).ExecuteUpdateAsync(x => x.SetProperty(m => m.CursoId, queda.Id));
                var ocupados = await _context.ClasesHorario.Where(c => c.CursoId == queda.Id).Select(c => new { c.Dia, c.BloqueJornadaId }).ToListAsync();
                foreach (var cl in await _context.ClasesHorario.Where(c => c.CursoId == dup.Id).ToListAsync())
                {
                    if (ocupados.Any(o => o.Dia == cl.Dia && o.BloqueJornadaId == cl.BloqueJornadaId)) _context.ClasesHorario.Remove(cl);
                    else cl.CursoId = queda.Id;
                }
                if (queda.ProfesorJefeId == null && dup.ProfesorJefeId != null) queda.ProfesorJefeId = dup.ProfesorJefeId;
                await _context.SaveChangesAsync();
                _context.Cursos.Remove(dup);
                await _context.SaveChangesAsync();
                cursosJuntados++;
            }
        }

        // 2) Apoderados/as repetidos (mismo RUT): sus hijos/as pasan a la cuenta más antigua; la repetida se desactiva
        var apos = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Rol == RolUsuario.Apoderado && u.Activo).ToListAsync();
        foreach (var g in apos.GroupBy(u => Rut.Normalizar(u.Rut)).Where(g => g.Count() > 1))
        {
            var queda = g.OrderBy(u => u.Id).First();
            var apoQueda = await _context.Apoderados.FirstOrDefaultAsync(a => a.UsuarioId == queda.Id);
            foreach (var dup in g.Where(u => u.Id != queda.Id))
            {
                var apoDup = await _context.Apoderados.FirstOrDefaultAsync(a => a.UsuarioId == dup.Id);
                if (apoDup != null)
                {
                    if (apoQueda == null) { apoDup.UsuarioId = queda.Id; apoQueda = apoDup; }
                    else
                    {
                        var hijosQueda = await _context.EstudiantesApoderados.Where(x => x.ApoderadoId == apoQueda.Id).Select(x => x.EstudianteId).ToListAsync();
                        foreach (var v in await _context.EstudiantesApoderados.Where(x => x.ApoderadoId == apoDup.Id).ToListAsync())
                        {
                            if (hijosQueda.Contains(v.EstudianteId)) _context.EstudiantesApoderados.Remove(v);
                            else v.ApoderadoId = apoQueda.Id;
                        }
                        await _context.SaveChangesAsync();
                        _context.Apoderados.Remove(apoDup);
                    }
                }
                dup.Activo = false;
                await _context.SaveChangesAsync();
                apoJuntados++;
            }
        }

        // 3) Alumnos/as repetidos (mismo RUT): se queda la cuenta con más datos; la repetida se desactiva
        var alus = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Rol == RolUsuario.Estudiante && u.Activo).ToListAsync();
        foreach (var g in alus.GroupBy(u => Rut.Normalizar(u.Rut)).Where(g => g.Count() > 1))
        {
            var queda = g.OrderBy(u => u.Id).First();
            var estQueda = await _context.Estudiantes.FirstOrDefaultAsync(e => e.UsuarioId == queda.Id);
            foreach (var dup in g.Where(u => u.Id != queda.Id))
            {
                var estDup = await _context.Estudiantes.FirstOrDefaultAsync(e => e.UsuarioId == dup.Id);
                if (estDup != null)
                {
                    if (estQueda == null) { estDup.UsuarioId = queda.Id; estQueda = estDup; }
                    else
                    {
                        // Apoderados del repetido pasan al que queda
                        var aposQueda = await _context.EstudiantesApoderados.Where(x => x.EstudianteId == estQueda.Id).Select(x => x.ApoderadoId).ToListAsync();
                        foreach (var v in await _context.EstudiantesApoderados.Where(x => x.EstudianteId == estDup.Id).ToListAsync())
                        {
                            if (aposQueda.Contains(v.ApoderadoId)) _context.EstudiantesApoderados.Remove(v);
                            else { v.EstudianteId = estQueda.Id; v.EsSostenedorPrincipal = false; }
                        }
                        await _context.SaveChangesAsync();
                        _context.Estudiantes.Remove(estDup);
                    }
                }
                dup.Activo = false;
                await _context.SaveChangesAsync();
                alumnosJuntados++;
            }
        }

        TempData["SuccessMessage"] = cursosJuntados + alumnosJuntados + apoJuntados == 0
            ? "✅ No había duplicados."
            : $"🧹 Listo: se juntaron {cursosJuntados} cursos repetidos, {alumnosJuntados} alumnos/as y {apoJuntados} apoderados/as repetidos.";
        return RedirectToAction(nameof(Index));
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
        // Ciclo de cada docente (básica, media...) según los cursos donde hace clases: así al elegir profesor/a jefe
        // de 1º básico salen primero los de básica, y de 3º medio, los de media
        var cicloCurso = Niveles.Ciclo(curso.Nombre);
        var clasesDe = await _context.ClasesHorario.AsNoTracking().Where(c => c.ProfesorId != null && c.Curso != null && c.Curso.AnioAcademico == curso.AnioAcademico)
            .Select(c => new { Prof = c.ProfesorId!.Value, c.Curso!.Nombre }).Distinct().ToListAsync();
        var jefes = await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == curso.AnioAcademico && c.ProfesorJefeId != null && c.Id != curso.Id)
            .Select(c => new { Prof = c.ProfesorJefeId!.Value, c.Nombre, c.Letra }).ToListAsync();
        ViewBag.CicloCurso = cicloCurso;
        _ = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var yoJefe);
        ViewBag.EsSuJefe = curso.ProfesorJefeId != null && curso.ProfesorJefeId == yoJefe;
        ViewBag.DelCiclo = clasesDe.Where(x => Niveles.Ciclo(x.Nombre) == cicloCurso).Select(x => x.Prof).Concat(jefes.Where(j => Niveles.Ciclo(j.Nombre) == cicloCurso).Select(j => j.Prof)).ToHashSet();
        ViewBag.YaEsJefe = jefes.GroupBy(j => j.Prof).ToDictionary(g => g.Key, g => $"{g.First().Nombre} {g.First().Letra}".Trim());
        return View(curso);
    }

    // 4. Formulario de Matrícula (GET)
    //    Matricula la administración y, durante un período de matrícula abierto, las personas que eligió la UTP/administración.
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Orientadora,Inspector")]
    [HttpGet]
    public async Task<IActionResult> Matricular()
    {
        var aniosPermitidos = await Matriculas.AniosQuePuedeAsync(_context, User);
        if (aniosPermitidos.Count == 0)
        {
            TempData["Error"] = "📝 La matrícula no está abierta para ti en este momento.";
            return RedirectToAction("Index", "Home");
        }
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
        // Años que esta persona puede matricular hoy (año actual y/o períodos abiertos, ej. Matrícula 2027)
        ViewBag.AniosMatricula = aniosPermitidos.ToArray();
        ViewBag.PeriodosAbiertos = (await Matriculas.CargarAsync(_context, User)).Where(Matriculas.Abierto).ToList();
        ViewBag.Especialidades = await EspecialidadesAsync(colegioId);
        return View();
    }

    // 5a. Revisión en vivo al escribir un RUT en la matrícula: ¿ya existe el alumno o el apoderado en este colegio?
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Orientadora,Inspector")]
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

    /// <summary>
    /// El profesor/a jefe sigue con su curso: al crear "2º Básico A" del año siguiente, queda el jefe que tuvo "1º Básico A".
    /// Al cerrar un ciclo vuelve a empezar: el de 8º básico pasa a 1º básico y el de 4º medio a 1º medio (misma letra).
    /// Si ese profesor/a ya es jefe de otro curso ese año, no se asigna (se elige a mano). Siempre se puede cambiar después.
    /// </summary>
    private async Task<int?> JefeQueSigueAsync(int colegioId, string nivel, string? letra, int anio)
    {
        var o = Niveles.Orden(nivel);
        if (o >= 99 || string.IsNullOrWhiteSpace(letra)) return null;
        var iniciaCiclo = o == 2 || o == 10;                 // 1º básico o 1º medio
        var previo = o == 2 ? 9 : o == 10 ? 13 : o - 1;      // de dónde viene: 8º básico, 4º medio o el nivel anterior
        if (previo < 0) return null;
        var l = letra.Trim().ToUpperInvariant();
        // Los cursos marcados "no sigue" (cortar) no arrastran a su profesor/a jefe
        var antes = (await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio - 1 && c.ProfesorJefeId != null && !c.JefeNoSigue).ToListAsync())
            .Where(c => Niveles.Orden(c.Nombre) == previo).ToList();
        var ocupados = (await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.ProfesorJefeId != null)
            .Select(c => c.ProfesorJefeId!.Value).ToListAsync()).ToHashSet();
        var activos = (await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente).Select(u => u.Id).ToListAsync()).ToHashSet();
        bool Libre(int? id) => id != null && activos.Contains(id.Value) && !ocupados.Contains(id.Value);

        var rotar = iniciaCiclo && await _context.Colegios.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == colegioId).Select(c => c.RotarLetraJefes).FirstOrDefaultAsync();
        if (!rotar)
        {
            // Mantiene su letra: 1º A (nivel anterior) → 2º A; 8º A → 1º A
            var prev = antes.FirstOrDefault(c => (c.Letra ?? "").Trim().ToUpperInvariant() == l);
            return Libre(prev?.ProfesorJefeId) ? prev!.ProfesorJefeId : null;
        }

        // Rotación: entre los profesores/as que terminan el ciclo y siguen libres, se elige a uno que no haya empezado
        // todavía con esta letra. Cada uno pasa por todas las letras antes de repetir (ej. A → C → B → A...).
        var candidatos = antes.Where(c => Libre(c.ProfesorJefeId)).Select(c => c.ProfesorJefeId!.Value).Distinct().ToList();
        if (candidatos.Count == 0) return null;
        var letrasNivel = (await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico >= anio - 1).ToListAsync())
            .Where(c => Niveles.Orden(c.Nombre) == o).Select(c => (c.Letra ?? "").Trim().ToUpperInvariant()).Append(l).Distinct().ToList();
        // Historial: con qué letra empezó cada uno sus ciclos anteriores (más reciente primero)
        var historial = (await _context.Cursos.AsNoTracking().Where(c => c.ColegioId == colegioId && c.AnioAcademico < anio && c.ProfesorJefeId != null && candidatos.Contains(c.ProfesorJefeId!.Value)).ToListAsync())
            .Where(c => Niveles.Orden(c.Nombre) == o).OrderByDescending(c => c.AnioAcademico)
            .GroupBy(c => c.ProfesorJefeId!.Value).ToDictionary(g => g.Key, g => g.Select(c => (c.Letra ?? "").Trim().ToUpperInvariant()).ToList());

        bool YaUso(int prof, string letraX)
        {
            if (!historial.TryGetValue(prof, out var usadas) || usadas.Count == 0) return false;
            // Vuelta actual: desde la última vez que completó todas las letras
            var vuelta = new List<string>();
            foreach (var u in usadas) { if (vuelta.Contains(u) || vuelta.Count >= letrasNivel.Count) break; vuelta.Add(u); }
            if (vuelta.Count >= letrasNivel.Count) vuelta = usadas.Take(1).ToList();   // ya pasó por todas: solo no repetir la última
            return vuelta.Contains(letraX);
        }

        // Sin la misma letra que ya tuvo; si todos ya la usaron, cualquiera menos el que la tuvo la última vez
        var elegibles = candidatos.Where(p => !YaUso(p, l)).ToList();
        if (elegibles.Count == 0) elegibles = candidatos.Where(p => !(historial.TryGetValue(p, out var u) && u.FirstOrDefault() == l)).ToList();
        if (elegibles.Count == 0) elegibles = candidatos;
        // Al azar entre los que corresponden (con semilla fija por colegio, año y curso: si se recalcula, da lo mismo)
        var rnd = new Random(colegioId * 7919 + anio * 31 + o * 17 + l[0]);
        return elegibles.OrderBy(p => p).ElementAt(rnd.Next(elegibles.Count));
    }

    /// <summary>
    /// "Cortar": el profesor/a jefe no sigue con este curso el año siguiente. Lo puede marcar quien asigna profesores jefe
    /// o el mismo profesor/a jefe (en ese caso se avisa a la UTP). Se puede deshacer.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CortarJefe(int cursoId, bool noSigue)
    {
        var colegioId = ColegioActual();
        var curso = await _context.Cursos.Include(c => c.ProfesorJefe).FirstOrDefaultAsync(c => c.Id == cursoId && c.ColegioId == colegioId);
        if (curso == null) return NotFound();
        _ = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var yo);
        var esJefe = curso.ProfesorJefeId == yo;
        if (!esJefe && !await Permisos.PuedeAsync(_context, User, Permisos.ProfJefe)) return RedirectToAction("AccesoDenegado", "Account");
        curso.JefeNoSigue = noSigue;
        if (esJefe && noSigue)
        {
            var utps = await _context.Usuarios.Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.UTP).Select(u => u.Id).ToListAsync();
            foreach (var u in utps)
                _context.Notificaciones.Add(new Notificacion { ColegioId = colegioId, UsuarioId = u, Titulo = "✂️ Profesor/a jefe no sigue",
                    Mensaje = $"{curso.ProfesorJefe?.Nombre} {curso.ProfesorJefe?.Apellido} no seguirá como jefe de {curso.Nombre} {curso.Letra} el próximo año. Hay que elegir a otro/a.",
                    Enlace = $"/Cursos/Alumnos/{curso.Id}", CreadaEnUtc = DateTime.UtcNow, EstadoCorreo = EstadoEnvioCorreo.SinCorreo });
        }
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = noSigue
            ? $"✂️ Listo: el próximo año {curso.Nombre} {curso.Letra} no sigue con el mismo profesor/a jefe" + (esJefe ? ". La UTP fue avisada." : ".")
            : $"🔗 Listo: el profesor/a jefe sigue con {curso.Nombre} {curso.Letra} el próximo año.";
        return RedirectToAction(nameof(Alumnos), new { id = cursoId });
    }

    /// <summary>La UTP, el director/a o el administrador eligen cómo vuelven los profesores/as jefe a 1º al terminar el ciclo.</summary>
    [Authorize(Roles = "SuperAdmin,Director,UTP")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarCicloJefes(bool rotar)
    {
        var colegioId = ColegioActual();
        var col = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        if (col == null) return NotFound();
        col.RotarLetraJefes = rotar;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = rotar
            ? "🔄 Listo: al terminar su ciclo, cada profesor/a jefe vuelve a 1º con otra letra, sin repetir hasta pasar por todas."
            : "📌 Listo: al terminar su ciclo, cada profesor/a jefe vuelve a 1º con su misma letra.";
        return RedirectToAction(nameof(PeriodosMatricula));
    }

    /// <summary>
    /// Importa los profesores/as jefe desde Excel (CSV). Columnas: Nivel; Letra; RUT profesor jefe.
    /// Un profesor/a puede ser jefe de un solo curso por año: si viene repetido, se avisa y se deja el primero.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> ImportarJefes(IFormFile? archivo)
    {
        if (!await Permisos.PuedeAsync(_context, User, Permisos.ProfJefe)) return RedirectToAction("AccesoDenegado", "Account");
        if (archivo == null || archivo.Length == 0) { TempData["Error"] = "Elige el archivo CSV."; return RedirectToAction(nameof(Index)); }
        var pl = await Csv.LeerAsync(archivo);
        int iNiv = pl.Col("nivel", "curso"), iLet = pl.Col("letra"), iRut = pl.Col("rut");
        if (iNiv < 0 || iLet < 0 || iRut < 0) { TempData["Error"] = "Faltan columnas. Usa la plantilla: Nivel, Letra y RUT profesor jefe."; return RedirectToAction(nameof(Index)); }

        var colegioId = ColegioActual();
        var anio = DateTime.Now.Year;
        var cursos = await _context.Cursos.Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync();
        var docentes = (await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente).ToListAsync())
            .GroupBy(u => Rut.Normalizar(u.Rut)).ToDictionary(g => g.Key, g => g.First());
        var usados = new Dictionary<int, string>();
        var errores = new List<string>();
        int asignados = 0, iguales = 0;
        for (var i = 0; i < pl.Filas.Count; i++)
        {
            var f = pl.Filas[i]; var fila = i + 2;
            string C(int k) => Csv.Planilla.Celda(f, k);
            if (C(iNiv).Length == 0 && C(iRut).Length == 0) continue;
            var letra = C(iLet).ToUpperInvariant();
            var curso = cursos.FirstOrDefault(c => Normal(c.Nombre) == Normal(C(iNiv)) && (c.Letra ?? "").Trim().ToUpperInvariant() == letra);
            if (curso == null) { errores.Add($"Fila {fila}: no existe {C(iNiv)} {letra} este año."); continue; }
            if (!Rut.TryValidar(C(iRut), out var rut, out _) || !docentes.TryGetValue(rut, out var doc)) { errores.Add($"Fila {fila}: no hay profesor/a con RUT {C(iRut)} en Personal."); continue; }
            if (usados.TryGetValue(doc.Id, out var otro)) { errores.Add($"Fila {fila}: {doc.Nombre} {doc.Apellido} ya quedó como jefe de {otro}."); continue; }
            usados[doc.Id] = $"{curso.Nombre} {curso.Letra}";
            if (curso.ProfesorJefeId == doc.Id) { iguales++; continue; }
            curso.ProfesorJefeId = doc.Id;
            asignados++;
        }
        // Si alguien quedó como jefe en la planilla, se quita de los cursos donde estaba antes (uno solo por año)
        foreach (var c in cursos.Where(c => c.ProfesorJefeId != null && usados.ContainsKey(c.ProfesorJefeId.Value) && usados[c.ProfesorJefeId.Value] != $"{c.Nombre} {c.Letra}"))
            c.ProfesorJefeId = null;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"👩‍🏫 Profesores/as jefe: {asignados} asignados" + (iguales > 0 ? $", {iguales} ya estaban" : "") + ".";
        if (errores.Count > 0) TempData["Error"] = $"{errores.Count} filas no se pudieron: " + string.Join(" ", errores.Take(8)) + (errores.Count > 8 ? " …" : "");
        return RedirectToAction(nameof(Index));
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
        nuevo.ProfesorJefeId = await JefeQueSigueAsync(colegioId, nuevo.Nombre, nuevo.Letra, anio);
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
    [Authorize(Roles = "SuperAdmin,Director,UTP,Docente,Orientadora,Inspector")]
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
        var aniosPermitidos = await Matriculas.AniosQuePuedeAsync(_context, User);
        if (aniosPermitidos.Count == 0) { TempData["Error"] = "📝 La matrícula no está abierta para ti en este momento."; return RedirectToAction("Index", "Home"); }
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
            // Año escolar: el elegido, solo si esta persona puede matricular ese año (año actual o un período abierto)
            var anio = anioEscolar is int ae && aniosPermitidos.Contains(ae) ? ae
                : aniosPermitidos.Contains(DateTime.Now.Year) ? DateTime.Now.Year : aniosPermitidos[0];
            // Mismo nivel aunque esté escrito distinto ("5° Básico" = "5º Básico")
            var delNivel = (await _context.Cursos.Include(c => c.Estudiantes)
                    .Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio).ToListAsync())
                .Where(c => Normal(c.Nombre) == Normal(nivelLimpio)).ToList();
            // Renovación: el alumno/a sigue con su mismo curso (misma letra) y así con su mismo profesor/a jefe.
            // Nuevos y traslados van al curso del nivel con más espacio.
            string? letraPrev = null;
            if (tipoMatricula == TipoMatricula.Renovacion)
            {
                var uPrev = (await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == colegioId && u.Rol == RolUsuario.Estudiante)
                    .Select(u => new { u.Id, u.Rut }).ToListAsync()).FirstOrDefault(u => Rut.Normalizar(u.Rut) == rutEstNorm);
                if (uPrev != null)
                    letraPrev = await _context.Estudiantes.AsNoTracking().Where(e => e.UsuarioId == uPrev.Id && e.Curso != null).Select(e => e.Curso!.Letra).FirstOrDefaultAsync();
                letraPrev = string.IsNullOrWhiteSpace(letraPrev) ? null : letraPrev.Trim().ToUpperInvariant();
            }
            var mismo = letraPrev == null ? null : delNivel.FirstOrDefault(c => (c.Letra ?? "").Trim().ToUpperInvariant() == letraPrev);
            var curso = mismo != null && mismo.Estudiantes.Count < mismo.CapacidadMaxima ? mismo
                : delNivel.OrderBy(c => c.Estudiantes.Count).ThenBy(c => c.Letra).FirstOrDefault();
            // Renovación sin su letra en el nivel: se crea con su misma letra
            if (letraPrev != null && mismo == null) curso = null;
            // Sin cursos en el nivel, o todos llenos: se crea solo el siguiente (A, B, C...)
            if (curso == null || curso.Estudiantes.Count >= curso.CapacidadMaxima)
            {
                var letraNueva = letraPrev != null && mismo == null ? letraPrev : SiguienteLetra(delNivel);
                var nuevo = new Curso { Nombre = curso?.Nombre ?? delNivel.FirstOrDefault()?.Nombre ?? nivelLimpio, Letra = letraNueva, CapacidadMaxima = curso?.CapacidadMaxima ?? 40, ColegioId = colegioId, AnioAcademico = anio };
                nuevo.ProfesorJefeId = await JefeQueSigueAsync(colegioId, nuevo.Nombre, nuevo.Letra, anio);
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