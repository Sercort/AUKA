using System.Security.Claims;
using System.Security.Cryptography;
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
