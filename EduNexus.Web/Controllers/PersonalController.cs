using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using EduNexus.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

[Authorize(Roles = "SuperAdmin,Administrador,Director,UTP")]
public class PersonalController : Controller
{
    private readonly ApplicationDbContext _context;

    public PersonalController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Vista de Lista de Funcionarios / Personal (Con Filtro de Búsqueda y Aislamiento por Colegio)
    [HttpGet]
    public async Task<IActionResult> Index(string? busqueda)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        if (!int.TryParse(colegioIdClaim, out int colegioId) || colegioId == 0)
        {
            return RedirectToAction("Login", "Account");
        }

        // Filtro estricto por ColegioId para garantizar privacidad entre instituciones
        var query = _context.Usuarios
            .Where(u => u.ColegioId == colegioId)
            .Where(u => u.Activo == true)
            .Where(u => u.Rol != RolUsuario.Estudiante && u.Rol != RolUsuario.Apoderado);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            string term = busqueda.Trim().ToLower();
            query = query.Where(u =>
                u.Rut.ToLower().Contains(term) ||
                u.Nombre.ToLower().Contains(term) ||
                u.Apellido.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.Telefono != null && u.Telefono.ToLower().Contains(term)) ||
                (u.Asignaturas != null && u.Asignaturas.ToLower().Contains(term)) ||
                (u.CargoInstitucional != null && u.CargoInstitucional.ToLower().Contains(term))
            );
        }

        ViewBag.BusquedaActual = busqueda;
        var personal = await query.OrderBy(u => u.Apellido).ToListAsync();

        return View(personal);
    }

    // 2. Formulario de Crear Funcionario (GET)
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Administrador,UTP")]
    public IActionResult Crear()
    {
        return View();
    }

    // 3. Procesar Crear Funcionario (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,UTP")]
    public async Task<IActionResult> Crear(string rut, string nombre, string apellido, string email, string? password, RolUsuario rol, string? cargoInstitucional, string? asignaturas, string? emailPersonal, string? telefono)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        if (!int.TryParse(colegioIdClaim, out int colegioId) || colegioId == 0)
        {
            return RedirectToAction("Login", "Account");
        }

        // A) Validar RUT
        if (string.IsNullOrWhiteSpace(rut) || !RutUtils.ValidarRut(rut))
        {
            ModelState.AddModelError("Rut", "El RUT ingresado no es válido. Verifica los números y el dígito verificador.");
            return View();
        }

        string rutLimpio = RutUtils.LimpiarRut(rut);

        // B) Validar duplicados de RUT y Email
        if (await _context.Usuarios.AnyAsync(u => u.Rut == rutLimpio))
        {
            ModelState.AddModelError("Rut", "Ya existe un funcionario registrado con este RUT.");
            return View();
        }

        if (await _context.Usuarios.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError("Email", "El correo electrónico ya se encuentra registrado en la plataforma.");
            return View();
        }

        // C) Generación y hash de clave provisoria
        string claveIngresada = string.IsNullOrWhiteSpace(password) ? "Edu123*" : password;
        var hasher = new PasswordHasher<string>();
        string passwordHash = hasher.HashPassword(email, claveIngresada);

        // D) Creación de la entidad Usuario asociada al Colegio
        var funcionario = new Usuario
        {
            Rut = rutLimpio,
            Nombre = nombre,
            Apellido = apellido,
            Email = email,
            PasswordHash = passwordHash,
            Rol = rol,
            CargoInstitucional = cargoInstitucional,
            Asignaturas = asignaturas,
            EmailPersonal = emailPersonal,
            Telefono = telefono,
            DebeCambiarPassword = true,
            ColegioId = colegioId,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        _context.Usuarios.Add(funcionario);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"✅ Funcionario {nombre} {apellido} registrado con éxito. Clave provisoria: {claveIngresada}";
        return RedirectToAction(nameof(Index));
    }

    // 4. Formulario de Editar Funcionario (GET)
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Administrador,UTP")]
    public async Task<IActionResult> Editar(int id)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && u.ColegioId == colegioId);
        if (usuario == null) return NotFound();

        return View(usuario);
    }

    // 5. Procesar Editar Funcionario (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,UTP")]
    public async Task<IActionResult> Editar(int id, string nombre, string apellido, string? emailPersonal, string? telefono, string? cargoInstitucional, string? asignaturas, RolUsuario rol)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && u.ColegioId == colegioId);
        if (usuario == null) return NotFound();

        // A) Actualizar datos del funcionario
        usuario.Nombre = nombre;
        usuario.Apellido = apellido;
        usuario.EmailPersonal = emailPersonal;
        usuario.Telefono = telefono;
        usuario.CargoInstitucional = cargoInstitucional;
        usuario.Asignaturas = asignaturas;
        usuario.Rol = rol;

        // B) Registro inteligente de asignaturas en la BD
        if (!string.IsNullOrWhiteSpace(asignaturas))
        {
            var cursoProgramacion = await _context.Cursos
                .FirstOrDefaultAsync(c => c.ColegioId == colegioId && (c.Nombre.Contains("Programación") || c.Nombre.Contains("TP") || c.Letra.Contains("TP")));

            var cursoPorDefecto = await _context.Cursos.FirstOrDefaultAsync(c => c.ColegioId == colegioId);
            int cursoIdValido = cursoPorDefecto?.Id ?? 1;

            var listaAsignaturas = asignaturas.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var asigNombre in listaAsignaturas)
            {
                string nombreLimpio = asigNombre.Trim();

                if (!string.IsNullOrEmpty(nombreLimpio))
                {
                    bool existe = await _context.Asignaturas.AnyAsync(a => a.Nombre.ToLower() == nombreLimpio.ToLower() && a.Curso.ColegioId == colegioId);

                    if (!existe)
                    {
                        string codigoAuto = nombreLimpio.Length >= 4
                            ? nombreLimpio.Substring(0, 4).ToUpper()
                            : nombreLimpio.ToUpper();

                        int idCursoFinal = (nombreLimpio.ToLower().Contains("programaci") || nombreLimpio.ToLower().Contains("c#") || nombreLimpio.ToLower().Contains("base de dato"))
                            ? (cursoProgramacion?.Id ?? cursoIdValido)
                            : cursoIdValido;

                        var nuevaAsignatura = new Asignatura
                        {
                            Nombre = nombreLimpio,
                            Codigo = codigoAuto,
                            CursoId = idCursoFinal,
                            DocenteId = usuario.Id
                        };

                        _context.Asignaturas.Add(nuevaAsignatura);
                    }
                }
            }
        }

        _context.Usuarios.Update(usuario);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"✅ Funcionario {usuario.Nombre} {usuario.Apellido} actualizado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    // 6. Desactivar Funcionario (Soft Delete)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin,Administrador,UTP")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var colegioIdClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioIdClaim, out int colegioId);

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id && u.ColegioId == colegioId);
        if (usuario != null)
        {
            usuario.Activo = false; // Desactivar en lugar de borrar
            _context.Usuarios.Update(usuario);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"🔒 El funcionario {usuario.Nombre} {usuario.Apellido} ha sido desactivado y movido al archivo de inactivos.";
        }

        return RedirectToAction(nameof(Index));
    }
}