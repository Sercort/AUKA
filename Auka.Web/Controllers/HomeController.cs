using System.Diagnostics;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;

    public HomeController(ApplicationDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        // Sin sesión: el portal público de AUKA (con botón "Ingresar" al login)
        if (User.Identity?.IsAuthenticated != true)
            return View("Portal");

        // El dueño de la plataforma tiene su propio panel
        if (User.IsInRole("AdminPlataforma"))
            return RedirectToAction("Index", "Plataforma");

        // Profesor/a: su inicio con sus clases de hoy, listas y evaluaciones
        if (User.IsInRole("Docente"))
            return RedirectToAction("Index", "Docente");

        // UTP: su panel académico es su pantalla de inicio
        if (User.IsInRole("UTP"))
            return RedirectToAction("Index", "Utp");

        // Administrador del colegio y director: panel de gestión del colegio (Plan Premium)
        if (User.IsInRole("SuperAdmin") || User.IsInRole("Director"))
            return View("PanelAdmin", await ArmarPanelAdminAsync());

        return View();
    }

    // ---------------- Panel del administrador del colegio ----------------
    private async Task<PanelAdminVm> ArmarPanelAdminAsync()
    {
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colegioId);
        var anio = DateTime.Now.Year;
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));

        var colegio = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == colegioId);

        var personal = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol != RolUsuario.Estudiante && u.Rol != RolUsuario.Apoderado && u.Rol != RolUsuario.AdminPlataforma)
            .GroupBy(u => u.Rol).Select(g => new { Rol = g.Key, N = g.Count() }).ToListAsync();
        var inspectorGeneral = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == colegioId && u.Activo && u.EsInspectorGeneral)
            .Select(u => u.Nombre + " " + u.Apellido).FirstOrDefaultAsync();

        var cursos = await _context.Cursos.AsNoTracking()
            .Where(c => c.ColegioId == colegioId && c.AnioAcademico == anio && c.Nombre != "Pacientes")
            .Select(c => new CursoTarjeta(c.Id, c.Nombre, c.Letra, c.Estudiantes.Count, c.CapacidadMaxima,
                c.ProfesorJefe != null ? c.ProfesorJefe.Nombre + " " + c.ProfesorJefe.Apellido : null))
            .ToListAsync();

        var clubes = await _context.Talleres.AsNoTracking()
            .Where(t => t.ColegioId == colegioId && t.Activo)
            .OrderBy(t => t.Nombre)
            .Select(t => new ClubTarjeta(t.Id, t.Nombre, t.Docente != null ? t.Docente.Nombre + " " + t.Docente.Apellido : t.DocenteCargo, t.Horario, t.Inscripciones.Count))
            .ToListAsync();

        var desde = DateTime.UtcNow.AddDays(-7);
        var anot = await _context.Anotaciones.AsNoTracking()
            .Where(a => a.Fecha >= desde && a.Estudiante != null && a.Estudiante.ColegioId == colegioId)
            .GroupBy(a => a.Tipo).Select(g => new { g.Key, N = g.Count() }).ToListAsync();

        int citSemana = 0, citPend = 0;
        if (colegio?.PlanPsicosocialActivo == true)
        {
            var finSemana = lunes.AddDays(6);
            citSemana = await _context.Citaciones.AsNoTracking().CountAsync(c => c.ColegioId == colegioId && c.Fecha >= lunes && c.Fecha <= finSemana);
            citPend = await _context.Citaciones.AsNoTracking().CountAsync(c => c.ColegioId == colegioId && c.Fecha >= hoy && c.Estado == EstadoCitacion.Pendiente);
        }

        int Rol(RolUsuario r) => personal.Where(p => p.Rol == r).Sum(p => p.N);
        return new PanelAdminVm
        {
            Colegio = colegio?.Nombre ?? "Tu colegio",
            EsAdministrador = User.IsInRole("SuperAdmin"),
            Psicosocial = colegio?.PlanPsicosocialActivo == true,
            // El resumen psicosocial del administrador solo existe si el dueño de la plataforma lo activó
            PsicoAdmin = colegio?.PlanPsicosocialActivo == true && colegio.PsicosocialAdministrador,
            Alumnos = cursos.Sum(c => c.Alumnos),
            Docentes = Rol(RolUsuario.Docente),
            Cursos = OrdenarCursos(cursos),
            Clubes = clubes,
            AnotPositivas = anot.Where(a => a.Key == TipoAnotacion.Positiva).Sum(a => a.N),
            AnotNegativas = anot.Where(a => a.Key == TipoAnotacion.Negativa).Sum(a => a.N),
            CitacionesSemana = citSemana,
            CitacionesPendientes = citPend,
            Personal = new List<(string, int, string)>
            {
                ("Docentes", Rol(RolUsuario.Docente), "👩‍🏫"),
                ("Directivos", Rol(RolUsuario.Director) + Rol(RolUsuario.SuperAdmin), "🏛️"),
                ("UTP", Rol(RolUsuario.UTP), "📚"),
                ("Inspectoría", Rol(RolUsuario.Inspector), "🧭"),
                ("Equipo psicosocial", Rol(RolUsuario.Psicologo) + Rol(RolUsuario.Psicopedagogo) + Rol(RolUsuario.TrabajadorSocial) + Rol(RolUsuario.Orientadora), "💙")
            },
            InspectorGeneral = inspectorGeneral,
            Permisos = Permisos.Leer(colegio?.Permisos, colegio?.ClubesPermitidos),
            ListaDocentes = await _context.Usuarios.AsNoTracking()
                .Where(u => u.ColegioId == colegioId && u.Activo && u.Rol == RolUsuario.Docente)
                .OrderBy(u => u.Apellido).Select(u => new OpcionVm(u.Id, u.Nombre + " " + u.Apellido)).ToListAsync(),
            PuedeProfJefe = await Permisos.PuedeAsync(_context, User, Permisos.ProfJefe),
            // Alertas de convivencia: lo malo (cursos en alerta o atención) y lo bueno (cursos destacados)
            AlertasCursos = (await Alertas.EstadoCursosAsync(_context, colegioId)).Values.Where(e => e.Nivel != Alertas.Nivel.Normal)
                .OrderByDescending(e => e.Nivel).ThenByDescending(e => e.Negativas).ToList(),
            ConfigAlertas = Alertas.Leer(colegio?.ConfigAlertas)
        };
    }

    // Orden natural de los cursos: prekínder, kínder, 1° a 8° básico, I a IV medio
    private static List<CursoTarjeta> OrdenarCursos(List<CursoTarjeta> l)
    {
        int Orden(string n)
        {
            var x = (n ?? "").ToLowerInvariant();
            if (x.Contains("pre")) return 0;
            if (x.Contains("nder") || x.StartsWith("nt")) return 1;
            if (x.Contains("medio"))
            {
                var r = System.Text.RegularExpressions.Regex.Match(x, @"^\s*(iv|iii|ii|i|\d)");
                return 20 + (r.Groups[1].Value switch { "i" or "1" => 1, "ii" or "2" => 2, "iii" or "3" => 3, "iv" or "4" => 4, _ => 5 });
            }
            var m = System.Text.RegularExpressions.Regex.Match(x, @"^\s*(\d+)");
            return m.Success ? 1 + int.Parse(m.Groups[1].Value) : 50;
        }
        return l.OrderBy(c => Orden(c.Nombre)).ThenBy(c => c.Letra).ToList();
    }

    // POST: límites de las alertas de anotaciones (por colegio)
    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarAlertas(double neg, int alumnos, int dias)
    {
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colegioId);
        var colegio = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        if (colegio == null) return NotFound();
        colegio.ConfigAlertas = Alertas.Escribir(new Alertas.Config(Math.Clamp(neg, 0.1, 10), Math.Clamp(alumnos, 1, 40), Math.Clamp(dias, 7, 120)));
        await _context.SaveChangesAsync();
        TempData["PanelOk"] = "Límites de alertas guardados.";
        return Redirect(Url.Action(nameof(Index)) + "#alertas");
    }

    // POST: el administrador elige quién más puede hacer cada cosa (casillas "p_clubes", "p_inscribir", "p_horarios", "p_profjefe")
    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarPermisos(IFormCollection form)
    {
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var colegioId);
        var colegio = await _context.Colegios.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == colegioId);
        if (colegio == null) return NotFound();
        var d = Permisos.Acciones.ToDictionary(a => a.Clave,
            a => new HashSet<string>(form["p_" + a.Clave].Where(r => r != null && a.Roles.Contains(r)).Select(r => r!)));
        colegio.Permisos = Permisos.Escribir(d);
        await _context.SaveChangesAsync();
        TempData["PanelOk"] = "Permisos guardados. Se aplican al tiro.";
        return Redirect(Url.Action(nameof(Index)) + "#permisos");
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
        // SEGURIDAD: solo existe en tu PC de desarrollo. En producción (internet por ngrok) no existe:
        // si no, cualquiera podría dejar la clave de un director en 123456.
        if (!_env.IsDevelopment()) return NotFound();

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

public record CursoTarjeta(int Id, string Nombre, string Letra, int Alumnos, int Capacidad, string? ProfesorJefe);
public record ClubTarjeta(int Id, string Nombre, string? Docente, string? Horario, int Inscritos);

public class PanelAdminVm
{
    public string Colegio { get; set; } = "";
    public bool EsAdministrador { get; set; }
    public bool Psicosocial { get; set; }
    public int Alumnos { get; set; }
    public int Docentes { get; set; }
    public List<CursoTarjeta> Cursos { get; set; } = new();
    public List<ClubTarjeta> Clubes { get; set; } = new();
    public int AnotPositivas { get; set; }
    public int AnotNegativas { get; set; }
    public int CitacionesSemana { get; set; }
    public int CitacionesPendientes { get; set; }
    public List<(string Nombre, int N, string Icono)> Personal { get; set; } = new();
    public string? InspectorGeneral { get; set; }
    public bool PsicoAdmin { get; set; }
    public Dictionary<string, HashSet<string>> Permisos { get; set; } = new();
    public List<OpcionVm> ListaDocentes { get; set; } = new();
    public bool PuedeProfJefe { get; set; }
    public List<Auka.Web.Services.Alertas.EstadoCurso> AlertasCursos { get; set; } = new();
    public Auka.Web.Services.Alertas.Config ConfigAlertas { get; set; } = new(1, 3, 30);
}

public record OpcionVm(int Id, string Nombre);
