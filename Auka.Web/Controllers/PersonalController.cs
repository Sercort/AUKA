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

    // 2. POST: CREAR FUNCIONARIO
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

        // A) Validación de RUT (Formato chileno)
        if (!Regex.IsMatch(rut ?? string.Empty, @"^[0-9]+-[0-9kK]{1}$"))
        {
            TempData["Error"] = "❌ El RUT ingresado no es válido. Formato requerido: 12345678-9";
            return RedirectToAction(nameof(Index));
        }

        // B) Verificar correo duplicado
        bool existeEmail = await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == emailInstitucional.Trim().ToLower());
        if (existeEmail)
        {
            TempData["Error"] = "❌ El correo institucional ya se encuentra registrado en el sistema.";
            return RedirectToAction(nameof(Index));
        }

        // C) Traspaso de Inspector General
        if (rol == RolUsuario.Inspector && esInspectorGeneral)
        {
            var inspectorPrevio = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Rol == RolUsuario.Inspector && u.EsInspectorGeneral && u.ColegioId == colegioId);
            if (inspectorPrevio != null)
            {
                inspectorPrevio.EsInspectorGeneral = false;
                _context.Usuarios.Update(inspectorPrevio);
            }
        }

        // D) Guardar nuevo usuario
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

        // E) Registrar Asignaturas en el catálogo (Resuelto CS8602 con validación directa)
        if (!string.IsNullOrWhiteSpace(asignaturasImpartidas))
        {
            var primerCurso = await _context.Cursos
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ColegioId == colegioId);
            int cursoIdValido = primerCurso != null ? primerCurso.Id : 1;

            var lista = asignaturasImpartidas.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in lista)
            {
                string asigNombre = item.Trim();
                bool existeAsig = await _context.Asignaturas
                    .AnyAsync(a => a.Nombre.ToLower() == asigNombre.ToLower());

                if (!existeAsig)
                {
                    _context.Asignaturas.Add(new Asignatura
                    {
                        Nombre = asigNombre,
                        Codigo = asigNombre.Length >= 4 ? asigNombre.Substring(0, 4).ToUpper() : asigNombre.ToUpper(),
                        CursoId = cursoIdValido,
                        DocenteId = nuevoPersonal.Id
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = $"✅ {nuevoPersonal.Nombre} {nuevoPersonal.Apellido} fue registrado con éxito. Correo: {nuevoPersonal.Email} (Clave provisoria: 123456).";
        return RedirectToAction(nameof(Index));
    }

    // 3. POST: ASIGNAR INSPECTOR GENERAL
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
    public async Task<IActionResult> AsignarInspectorGeneral(int id)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var inspectores = await _context.Usuarios
            .Where(u => u.Rol == RolUsuario.Inspector && (u.ColegioId == colegioId || colegioId == 0))
            .ToListAsync();

        foreach (var insp in inspectores)
        {
            insp.EsInspectorGeneral = (insp.Id == id);
            _context.Usuarios.Update(insp);
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "✅ Se ha reasignado el cargo de Inspector General con éxito.";
        return RedirectToAction(nameof(Index));
    }

    // 4. POST: DESACTIVAR (Soft Delete)
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

    // 5. POST: REACTIVAR
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