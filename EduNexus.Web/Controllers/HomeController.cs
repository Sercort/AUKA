using System.Diagnostics;
using EduNexus.Application.Entities;
using EduNexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Web.Controllers;

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

    // 🔑 MÉTODO DE EMERGENCIA PARA DESBLOQUEAR AL ADMINISTRADOR
    [HttpGet]
    [AllowAnonymous]
    [Route("reset-admin")]
    public async Task<IActionResult> ResetAdminPass()
    {
        var admin = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Rol == RolUsuario.Director || u.Rol == RolUsuario.SuperAdmin || u.Rol == RolUsuario.UTP);

        if (admin != null)
        {
            var hasher = new PasswordHasher<string>();
            admin.PasswordHash = hasher.HashPassword(admin.Email, "123456");
            admin.DebeCambiarPassword = false;
            admin.Activo = true;

            await _context.SaveChangesAsync();
            return Content($"✅ ¡LISTO! La contraseña de '{admin.Email}' fue cambiada con éxito a: 123456");
        }

        return Content("❌ No se encontró ningún usuario Administrador en la base de datos.");
    }
}