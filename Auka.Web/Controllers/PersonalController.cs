using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize]
public class PersonalController : Controller
{
    private readonly ApplicationDbContext _context;

    // Roles que se pueden asignar a un funcionario desde esta pantalla
    private static readonly HashSet<RolUsuario> RolesDelPersonal = new()
    {
        RolUsuario.Director, RolUsuario.UTP, RolUsuario.Docente, RolUsuario.Inspector,
        RolUsuario.Psicologo, RolUsuario.Psicopedagogo, RolUsuario.TrabajadorSocial, RolUsuario.Orientadora
    };

    // Roles que solo existen en colegios con el módulo Psicosocial activo
    private static readonly HashSet<RolUsuario> RolesSoloPsicosocial = new()
    {
        RolUsuario.TrabajadorSocial, RolUsuario.Orientadora
    };

    // Plan Psicosocial: solo se puede agregar personal del área psicosocial y profesores/as jefe (guía)
    private static readonly RolUsuario[] AreaPsicosocial =
    {
        RolUsuario.Psicologo, RolUsuario.Psicopedagogo, RolUsuario.TrabajadorSocial, RolUsuario.Orientadora, RolUsuario.Docente
    };

    private bool SoloPsicosocial() =>
        User.FindFirst("PlanPsicosocial")?.Value == "true" && User.FindFirst("PlanPremium")?.Value == "false";

    public PersonalController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int ColegioActual() => int.TryParse(User.FindFirst("ColegioId")?.Value, out var id) ? id : 0;

    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    // Valida qué roles puede asignar quien está usando la pantalla
    private bool PuedeAsignarRol(RolUsuario rol, out string error)
    {
        error = "";
        if (rol == RolUsuario.SuperAdmin)
        {
            if (!User.IsInRole("SuperAdmin")) { error = "Solo el administrador del colegio puede crear a otro administrador."; return false; }
            return true;
        }
        if (!RolesDelPersonal.Contains(rol)) { error = "Ese rol no se puede asignar desde esta pantalla."; return false; }
        if (SoloPsicosocial() && !AreaPsicosocial.Contains(rol))
        {
            error = "Tu Plan Psicosocial solo permite agregar psicólogos/as, trabajadores/as sociales, orientadores/as, psicopedagogos/as y profesores/as jefe. Los demás cargos son del Plan Premium.";
            return false;
        }
        if (RolesSoloPsicosocial.Contains(rol) && User.FindFirst("PlanPsicosocial")?.Value != "true")
        {
            error = "El módulo Psicosocial no está activo en este colegio.";
            return false;
        }
        return true;
    }

    private static string GenerarClave()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)]).ToArray());
    }

    // 1. VISTA PRINCIPAL: Directorio de Personal
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string tab = "activos")
    {
        var colegioId = ColegioActual();

        // El directorio es solo del personal: no incluye alumnos/as ni apoderados/as
        var query = _context.Usuarios
            .Where(u => u.ColegioId == colegioId || colegioId == 0)
            .Where(u => u.Rol != RolUsuario.Estudiante && u.Rol != RolUsuario.Apoderado && u.Rol != RolUsuario.AdminPlataforma)
            .AsQueryable();

        if (SoloPsicosocial())
        {
            var permitidos = AreaPsicosocial;
            query = query.Where(u => permitidos.Contains(u.Rol));
        }

        bool mostrarActivos = (tab ?? "activos").ToLower() != "inactivos";
        query = query.Where(u => u.Activo == mostrarActivos);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLower();
            query = query.Where(u =>
                u.Rut.ToLower().Contains(term) ||
                u.Nombre.ToLower().Contains(term) ||
                u.Apellido.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                (u.EmailPersonal != null && u.EmailPersonal.ToLower().Contains(term)) ||
                (u.Telefono != null && u.Telefono.ToLower().Contains(term)) ||
                (u.Direccion != null && u.Direccion.ToLower().Contains(term))
            );
        }

        var listaUsuarios = await query.OrderBy(u => u.Apellido).ToListAsync();

        ViewBag.SearchTerm = search;
        ViewBag.TabActual = tab;

        return View(listaUsuarios);
    }

    // 2. GET: FORMULARIO DE EDICIÓN DE FUNCIONARIO
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Editar(int id)
    {
        var colegioId = ColegioActual();

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && (u.ColegioId == colegioId || colegioId == 0));

        if (usuario == null)
        {
            TempData["Error"] = "❌ El funcionario no fue encontrado en la base de datos.";
            return RedirectToAction(nameof(Index));
        }

        return View(usuario);
    }

    // 3. POST: PROCESAR EDICIÓN DE FUNCIONARIO
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Editar(
        int id, string nombre, string apellido,
        string? emailPersonal, string? telefono, string? direccion,
        RolUsuario rol, string? cargoEspecifico, string? asignaturasImpartidas)
    {
        var colegioId = ColegioActual();

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && (u.ColegioId == colegioId || colegioId == 0));

        if (usuario == null)
        {
            TempData["Error"] = "❌ El funcionario no fue encontrado.";
            return RedirectToAction(nameof(Index));
        }

        // Si el rol cambia, se valida que se pueda asignar
        if (rol != usuario.Rol && !PuedeAsignarRol(rol, out var errorRol))
        {
            TempData["Error"] = $"❌ {errorRol}";
            return RedirectToAction(nameof(Index));
        }

        // Actualización de campos de contacto y perfil
        usuario.Nombre = nombre.Trim();
        usuario.Apellido = apellido.Trim();
        usuario.EmailPersonal = emailPersonal?.Trim().ToLower();
        usuario.Telefono = telefono?.Trim();
        usuario.Direccion = direccion?.Trim();
        usuario.Rol = rol;
        usuario.CargoInstitucional = string.IsNullOrWhiteSpace(cargoEspecifico) ? rol.ToString() : cargoEspecifico.Trim();
        usuario.Asignaturas = asignaturasImpartidas?.Trim();

        _context.Usuarios.Update(usuario);
        await _context.SaveChangesAsync();

        // Registro / Sincronización automática de asignaturas agregadas o editadas
        if (!string.IsNullOrWhiteSpace(asignaturasImpartidas))
        {
            var primerCurso = await _context.Cursos
                .FirstOrDefaultAsync(c => c.ColegioId == colegioId);

            if (primerCurso == null)
            {
                primerCurso = new Curso
                {
                    Nombre = "General Institucional",
                    Letra = "A",
                    CapacidadMaxima = 45,
                    AnioAcademico = DateTime.UtcNow.Year,
                    ColegioId = colegioId > 0 ? colegioId : 1
                };
                _context.Cursos.Add(primerCurso);
                await _context.SaveChangesAsync();
            }

            var lista = asignaturasImpartidas.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in lista)
            {
                string asigNombre = item.Trim();
                bool existeAsig = await _context.Asignaturas
                    .AnyAsync(a => a.Nombre.ToLower() == asigNombre.ToLower() && a.DocenteId == usuario.Id);

                if (!existeAsig)
                {
                    _context.Asignaturas.Add(new Asignatura
                    {
                        Nombre = asigNombre,
                        Codigo = asigNombre.Length >= 4 ? asigNombre.Substring(0, 4).ToUpper() : asigNombre.ToUpper(),
                        CursoId = primerCurso.Id,
                        DocenteId = usuario.Id
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = $"✅ Datos de {usuario.Nombre} {usuario.Apellido} actualizados con éxito.";
        return RedirectToAction(nameof(Index));
    }

    // 4. POST: CREAR FUNCIONARIO (con clave provisoria distinta para cada persona)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Crear(
        string rut, string nombre, string apellido,
        string emailInstitucional, string? emailPersonal,
        string? telefono, string? direccion, RolUsuario rol,
        string? cargoEspecifico, string? asignaturasImpartidas, bool esInspectorGeneral)
    {
        var colegioId = ColegioActual();
        colegioId = colegioId > 0 ? colegioId : 1;

        if (!Regex.IsMatch(rut ?? string.Empty, @"^[0-9]+-[0-9kK]{1}$"))
        {
            TempData["Error"] = "❌ El RUT ingresado no es válido. Formato requerido: 12345678-9";
            return RedirectToAction(nameof(Index));
        }

        if (!PuedeAsignarRol(rol, out var errorRol))
        {
            TempData["Error"] = $"❌ {errorRol}";
            return RedirectToAction(nameof(Index));
        }

        // El correo es el usuario de ingreso: debe ser único en TODOS los colegios
        var correo = emailInstitucional.Trim().ToLower();
        bool existeEmail = await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == correo);
        if (existeEmail)
        {
            TempData["Error"] = "❌ El correo institucional ya se encuentra registrado en el sistema.";
            return RedirectToAction(nameof(Index));
        }

        var clave = GenerarClave();
        var hasher = new PasswordHasher<string>();
        var nuevoPersonal = new Usuario
        {
            Rut = rut!.Trim(),
            Nombre = nombre.Trim(),
            Apellido = apellido.Trim(),
            Email = correo,
            EmailPersonal = emailPersonal?.Trim().ToLower(),
            Telefono = telefono?.Trim(),
            Direccion = direccion?.Trim(),
            Rol = rol,
            CargoInstitucional = string.IsNullOrWhiteSpace(cargoEspecifico) ? rol.ToString() : cargoEspecifico.Trim(),
            EsInspectorGeneral = (rol == RolUsuario.Inspector) && esInspectorGeneral,
            Asignaturas = asignaturasImpartidas?.Trim(),
            ColegioId = colegioId,
            Activo = true,
            DebeCambiarPassword = true,
            FechaCreacion = DateTime.UtcNow
        };

        nuevoPersonal.PasswordHash = hasher.HashPassword(nuevoPersonal.Email, clave);
        _context.Usuarios.Add(nuevoPersonal);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] =
            $"✅ {nuevoPersonal.Nombre} {nuevoPersonal.Apellido} fue registrado/a. Correo: {nuevoPersonal.Email} · " +
            $"Clave provisoria: {clave} (anótala ahora: no se vuelve a mostrar; deberá cambiarla al iniciar sesión).";
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------
    // IMPORTAR PERSONAL DESDE PLANILLA (CSV): Nombre; Apellido; RUT; Correo; Correo personal; Teléfono; Cargo; Asignaturas
    // ---------------------------------------------------------------

    private static string SinTildes(string t)
    {
        var d = (t ?? "").Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Trim();
    }

    private static RolUsuario? RolDesdeTexto(string? texto)
    {
        var t = SinTildes(texto ?? "");
        if (t.Length == 0) return null;
        if (t.Contains("psicopedag")) return RolUsuario.Psicopedagogo;
        if (t.Contains("psicol")) return RolUsuario.Psicologo;
        if (t.Contains("social")) return RolUsuario.TrabajadorSocial;
        if (t.Contains("orient")) return RolUsuario.Orientadora;
        if (t.Contains("utp") || t.Contains("coordinador")) return RolUsuario.UTP;
        if (t.Contains("direct")) return RolUsuario.Director;
        if (t.Contains("inspect") || t.Contains("convivencia")) return RolUsuario.Inspector;
        if (t.Contains("docente") || t.Contains("profesor") || t.Contains("guia")) return RolUsuario.Docente;
        return null;
    }

    // POST: /Personal/ImportarPersonal
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> ImportarPersonal(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0) return BadRequest(new { error = "Elige un archivo CSV." });
        if (archivo.Length > 2_000_000) return BadRequest(new { error = "El archivo es demasiado grande." });
        if (!archivo.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Por ahora solo se lee CSV. En Excel: Archivo → Guardar como → CSV." });

        var lineas = new List<string>();
        using (var lector = new StreamReader(archivo.OpenReadStream(), Encoding.UTF8, true))
        {
            string? linea;
            while ((linea = await lector.ReadLineAsync()) != null)
                if (!string.IsNullOrWhiteSpace(linea)) lineas.Add(linea);
        }
        if (lineas.Count < 2) return BadRequest(new { error = "El archivo no tiene filas de personal." });
        if (lineas.Count > 501) return BadRequest(new { error = "Máximo 500 funcionarios/as por archivo." });

        var sep = lineas[0].Count(c => c == ';') >= lineas[0].Count(c => c == ',') ? ';' : ',';
        var cab = lineas[0].Split(sep).Select(h => SinTildes(h.Trim().Trim('"').TrimStart('\ufeff'))).ToList();
        int Col(params string[] claves) => cab.FindIndex(h => claves.Any(k => h.StartsWith(k)));
        int iNom = Col("nombre"), iApe = Col("apellido"), iRut = Col("rut"), iCor = Col("correo institucional", "correo"),
            iPer = Col("correo personal"), iTel = Col("tel"), iCar = Col("cargo", "rol"), iAsi = Col("asignatura");
        if (iNom < 0 || iRut < 0 || iCor < 0 || iCar < 0)
            return BadRequest(new { error = "Faltan columnas. Necesito al menos: Nombre, RUT, Correo y Cargo." });
        string Celda(string[] p, int i) => i >= 0 && i < p.Length ? p[i].Trim().Trim('"').Trim() : "";

        var colegioId = ColegioActual();
        colegioId = colegioId > 0 ? colegioId : 1;
        var hasher = new PasswordHasher<string>();
        var errores = new List<string>();
        var credenciales = new List<object>();
        int creados = 0, omitidos = 0;
        var vistosCorreo = new HashSet<string>();
        var vistosRut = new HashSet<string>();

        var ruts = (await _context.Usuarios.Where(u => u.ColegioId == colegioId).Select(u => u.Rut).ToListAsync())
            .Select(r => r.Replace(".", "").Replace(" ", "").ToUpperInvariant()).ToHashSet();
        var correosExistentes = (await _context.Usuarios.IgnoreQueryFilters().Select(u => u.Email.ToLower()).ToListAsync()).ToHashSet();

        for (int i = 1; i < lineas.Count; i++)
        {
            var p = lineas[i].Split(sep);
            int fila = i + 1;
            var nombre = Celda(p, iNom);
            var apellido = Celda(p, iApe);
            if (iApe < 0 || apellido.Length == 0)
            {
                // Sin columna de apellido: el primer nombre queda como nombre y el resto como apellidos
                var partes = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length >= 3) { nombre = string.Join(' ', partes.Take(partes.Length - 2)); apellido = string.Join(' ', partes.Skip(partes.Length - 2)); }
                else if (partes.Length == 2) { nombre = partes[0]; apellido = partes[1]; }
            }
            var rut = Celda(p, iRut).Replace(".", "").Replace(" ", "").ToUpperInvariant();
            var correo = Celda(p, iCor).ToLowerInvariant();
            var rolTxt = Celda(p, iCar);

            if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(rut)) { continue; }
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(apellido)) { errores.Add($"Fila {fila}: falta nombre o apellido."); continue; }
            if (!Regex.IsMatch(rut, @"^[0-9]+-[0-9K]$")) { errores.Add($"Fila {fila}: RUT no válido ({rut}). Formato: 12345678-9."); continue; }
            if (!Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) { errores.Add($"Fila {fila}: correo no válido."); continue; }
            var rol = RolDesdeTexto(rolTxt);
            if (rol == null) { errores.Add($"Fila {fila}: no reconozco el cargo \"{rolTxt}\"."); continue; }
            if (!PuedeAsignarRol(rol.Value, out var errRol)) { errores.Add($"Fila {fila} ({nombre} {apellido}): {errRol}"); continue; }

            if (ruts.Contains(rut) || !vistosRut.Add(rut)) { omitidos++; errores.Add($"Fila {fila}: el RUT {rut} ya existe en este colegio (se omitió)."); continue; }
            if (correosExistentes.Contains(correo) || !vistosCorreo.Add(correo)) { omitidos++; errores.Add($"Fila {fila}: el correo {correo} ya está registrado (se omitió)."); continue; }

            var clave = GenerarClave();
            var u = new Usuario
            {
                Rut = rut,
                Nombre = nombre,
                Apellido = apellido,
                Email = correo,
                EmailPersonal = string.IsNullOrWhiteSpace(Celda(p, iPer)) ? null : Celda(p, iPer).ToLowerInvariant(),
                Telefono = string.IsNullOrWhiteSpace(Celda(p, iTel)) ? null : Celda(p, iTel),
                Rol = rol.Value,
                CargoInstitucional = rol.Value.ToString(),
                Asignaturas = rol == RolUsuario.Docente && !string.IsNullOrWhiteSpace(Celda(p, iAsi)) ? Celda(p, iAsi) : null,
                ColegioId = colegioId,
                Activo = true,
                DebeCambiarPassword = true,
                FechaCreacion = DateTime.UtcNow
            };
            u.PasswordHash = hasher.HashPassword(u.Email, clave);
            _context.Usuarios.Add(u);
            credenciales.Add(new { rut, n = $"{nombre} {apellido}", e = correo, c = clave, r = rol.Value.ToString() });
            creados++;
        }

        if (creados > 0) await _context.SaveChangesAsync();
        return Json(new { creados, omitidos, totalErrores = errores.Count, errores = errores.Take(30), credenciales });
    }

    // 5. POST: DESACTIVAR
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Desactivar(int id)
    {
        var colegioId = ColegioActual();
        var user = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && (u.ColegioId == colegioId || colegioId == 0));
        if (user != null)
        {
            if (user.Id == UsuarioActualId())
            {
                TempData["Error"] = "❌ No puedes desactivar tu propia cuenta.";
                return RedirectToAction(nameof(Index));
            }
            user.Activo = false;
            _context.Usuarios.Update(user);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"⚠️ El funcionario {user.Nombre} {user.Apellido} fue movido a Inactivos.";
        }
        return RedirectToAction(nameof(Index));
    }

    // 6. POST: REACTIVAR
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Reactivar(int id)
    {
        var colegioId = ColegioActual();
        var user = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && (u.ColegioId == colegioId || colegioId == 0));
        if (user != null)
        {
            user.Activo = true;
            _context.Usuarios.Update(user);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"✅ El funcionario {user.Nombre} {user.Apellido} fue reactivado.";
        }
        return RedirectToAction(nameof(Index), new { tab = "inactivos" });
    }

    // 7. POST: HACER INSPECTOR GENERAL
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> AsignarInspectorGeneral(int id)
    {
        var colegioId = ColegioActual();
        var user = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && u.Rol == RolUsuario.Inspector && (u.ColegioId == colegioId || colegioId == 0));
        if (user != null)
        {
            user.EsInspectorGeneral = true;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"⭐ {user.Nombre} {user.Apellido} fue designado/a Inspector/a General.";
        }
        return RedirectToAction(nameof(Index));
    }

    // 8. POST: GENERAR NUEVA CLAVE PROVISORIA (para quien no puede entrar)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> RestablecerClave(int id)
    {
        var colegioId = ColegioActual();
        var user = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && (u.ColegioId == colegioId || colegioId == 0));
        if (user == null || user.Rol == RolUsuario.Estudiante || user.Rol == RolUsuario.Apoderado || user.Rol == RolUsuario.AdminPlataforma)
        {
            TempData["Error"] = "❌ No se encontró al funcionario.";
            return RedirectToAction(nameof(Index));
        }
        var clave = GenerarClave();
        user.PasswordHash = new PasswordHasher<string>().HashPassword(user.Email, clave);
        user.DebeCambiarPassword = true;
        user.Activo = true;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"🔑 Nueva clave de {user.Nombre} {user.Apellido}: correo {user.Email} · clave provisoria {clave} (anótala: no se vuelve a mostrar).";
        return RedirectToAction(nameof(Index));
    }
}
