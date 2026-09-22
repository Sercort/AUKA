using System.Diagnostics;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    // 🔑 MÉTODO DE RESCATE: CREA O DESBLOQUEA AL ADMINISTRADOR EN POSTGRESQL
    [HttpGet]
    [AllowAnonymous]
    [Route("reset-admin")]
    public async Task<IActionResult> ResetAdminPass()
    {
        // 1. Buscar si ya existe algún Administrador o Director
        var admin = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Rol == RolUsuario.Director || u.Rol == RolUsuario.SuperAdmin || u.Rol == RolUsuario.UTP || u.Email == "admin@auka.cl");

        var hasher = new PasswordHasher<string>();

        // 2. Si NO existe (Base de datos limpia), crear el Colegio por defecto y el Usuario inicial
        if (admin == null)
        {
            var colegio = await _context.Colegios.FirstOrDefaultAsync();
            if (colegio == null)
            {
                colegio = new Colegio
                {
                    Nombre = "Colegio Institucional AUKA",
                    Telefono = "+56912345678",
                    Direccion = "Av. Principal 123",
                    Activo = true
                };
                _context.Colegios.Add(colegio);
                await _context.SaveChangesAsync();
            }

            admin = new Usuario
            {
                Rut = "12345678-9",
                Nombre = "Administrador",
                Apellido = "General",
                Email = "admin@auka.cl",
                Rol = RolUsuario.Director,
                ColegioId = colegio.Id,
                Activo = true,
                DebeCambiarPassword = false,
                FechaCreacion = DateTime.UtcNow
            };

            admin.PasswordHash = hasher.HashPassword(admin.Email, "123456");

            _context.Usuarios.Add(admin);
            await _context.SaveChangesAsync();

            return Content("✅ ¡Base de Datos AukaDb en PostgreSQL lista!\n\nSe creó el usuario Administrador con éxito:\n\nCorreo: admin@auka.cl\nContraseña: 123456");
        }

        // 3. Si YA existe, restablecer su contraseña a 123456
        admin.PasswordHash = hasher.HashPassword(admin.Email, "123456");
        admin.DebeCambiarPassword = false;
        admin.Activo = true;

        _context.Usuarios.Update(admin);
        await _context.SaveChangesAsync();

        return Content($"✅ ¡LISTO! La contraseña de '{admin.Email}' fue restablecida con éxito a: 123456");
    }
}