using System.Security.Claims;
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

    public PersonalController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. VISTA PRINCIPAL: Directorio de Personal
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string tab = "activos")
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var query = _context.Usuarios
            .Where(u => u.ColegioId == colegioId || colegioId == 0)
            .AsQueryable();

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
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

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
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && (u.ColegioId == colegioId || colegioId == 0));

        if (usuario == null)
        {
            TempData["Error"] = "❌ El funcionario no fue encontrado.";
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

    // 4. POST: CREAR FUNCIONARIO
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Crear(
        string rut, string nombre, string apellido,
        string emailInstitucional, string? emailPersonal,
        string? telefono, string? direccion, RolUsuario rol,
        string? cargoEspecifico, string? asignaturasImpartidas, bool esInspectorGeneral)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);
        colegioId = colegioId > 0 ? colegioId : 1;

        if (!Regex.IsMatch(rut ?? string.Empty, @"^[0-9]+-[0-9kK]{1}$"))
        {
            TempData["Error"] = "❌ El RUT ingresado no es válido. Formato requerido: 12345678-9";
            return RedirectToAction(nameof(Index));
        }

        bool existeEmail = await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == emailInstitucional.Trim().ToLower());
        if (existeEmail)
        {
            TempData["Error"] = "❌ El correo institucional ya se encuentra registrado en el sistema.";
            return RedirectToAction(nameof(Index));
        }

        var hasher = new PasswordHasher<string>();
        var nuevoPersonal = new Usuario
        {
            Rut = rut.Trim(),
            Nombre = nombre.Trim(),
            Apellido = apellido.Trim(),
            Email = emailInstitucional.Trim().ToLower(),
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

        nuevoPersonal.PasswordHash = hasher.HashPassword(nuevoPersonal.Email, "123456");
        _context.Usuarios.Add(nuevoPersonal);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"✅ {nuevoPersonal.Nombre} {nuevoPersonal.Apellido} fue registrado con éxito.";
        return RedirectToAction(nameof(Index));
    }

    // 5. POST: DESACTIVAR
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> Desactivar(int id)
    {
        var user = await _context.Usuarios.FindAsync(id);
        if (user != null)
        {
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
        var user = await _context.Usuarios.FindAsync(id);
        if (user != null)
        {
            user.Activo = true;
            _context.Usuarios.Update(user);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"✅ El funcionario {user.Nombre} {user.Apellido} fue reactivado.";
        }
        return RedirectToAction(nameof(Index), new { tab = "inactivos" });
    }
}