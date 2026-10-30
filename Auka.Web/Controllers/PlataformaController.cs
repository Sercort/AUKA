using System.Globalization;
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

/// <summary>
/// Panel del dueño de la plataforma (AdminPlataforma): suscripciones, planes, vencimientos, contactos y mensajes.
/// Privacidad: aquí solo hay datos comerciales y CANTIDADES. Nunca fichas, bitácoras, anotaciones ni citaciones.
/// </summary>
[Authorize(Roles = "AdminPlataforma")]
public class PlataformaController : Controller
{
    private readonly ApplicationDbContext _context;
    public PlataformaController(ApplicationDbContext context) => _context = context;

    private static readonly CultureInfo Cl = new("es-CL");
    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    private static string GenerarClave()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)]).ToArray());
    }

    private static string PlanDe(Colegio c) =>
        c.PlanPsicosocialActivo && c.SoloPsicosocial ? "psicosocial" : c.PlanPsicosocialActivo ? "premium" : "institucional";

    private static string TextoPlan(string plan) => plan switch
    {
        "psicosocial" => "Plan Psicosocial",
        "premium" => "Plan Premium",
        _ => "Institucional"
    };

    private static void AplicarPlan(Colegio c, string plan)
    {
        switch ((plan ?? "").ToLowerInvariant())
        {
            case "psicosocial": c.PlanPsicosocialActivo = true; c.SoloPsicosocial = true; break;
            case "institucional": c.PlanPsicosocialActivo = false; c.SoloPsicosocial = false; break;
            default: c.PlanPsicosocialActivo = true; c.SoloPsicosocial = false; break;
        }
    }

    // activo | porvencer (30 días o menos) | vencido | suspendido | baja
    private static string EstadoDe(Colegio c, DateTime hoy)
    {
        if (!c.Activo) return c.FechaBaja != null ? "baja" : "suspendido";
        if (c.FechaVencimiento != null)
        {
            var d = (c.FechaVencimiento.Value.Date - hoy.Date).Days;
            if (d < 0) return "vencido";
            if (d <= 30) return "porvencer";
        }
        return "activo";
    }

    private static DateTime? Fecha(string? texto) =>
        DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null;

    private static string? Limpio(string? t, int max) { t = (t ?? "").Trim(); return t.Length == 0 ? null : (t.Length > max ? t[..max] : t); }

    private void Log(int colegioId, string tipo, string detalle) =>
        _context.HistorialColegios.Add(new HistorialColegio { ColegioRefId = colegioId, Tipo = tipo, Detalle = detalle, PorUsuarioId = UsuarioActualId(), FechaUtc = DateTime.UtcNow });

    // ---------------- Páginas ----------------
    [HttpGet] public IActionResult Index() => View();
    [HttpGet] public IActionResult Colegios() => View();
    [HttpGet] public IActionResult Mensajes() => View();

    // ---------------- Datos del panel (se consulta cada 30 s) ----------------
    [HttpGet]
    public async Task<IActionResult> Datos()
    {
        var hoy = DateTime.UtcNow;
        var colegios = await _context.Colegios.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        var us = await _context.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Activo && u.Rol != RolUsuario.AdminPlataforma).Select(u => new { u.ColegioId, u.Rol }).ToListAsync();

        var filas = colegios.Select(c => new
        {
            c.Id,
            c.Nombre,
            plan = PlanDe(c),
            estado = EstadoDe(c, hoy),
            activo = c.Activo,
            personas = us.Count(u => u.ColegioId == c.Id),
            alumnos = us.Count(u => u.ColegioId == c.Id && u.Rol == RolUsuario.Estudiante),
            precio = c.PrecioMensual,
            vence = c.FechaVencimiento,
            dias = c.FechaVencimiento == null ? (int?)null : (c.FechaVencimiento.Value.Date - hoy.Date).Days,
            contacto = c.ContactoNombre
        }).ToList();

        var activos = colegios.Where(c => c.Activo).ToList();
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Altas y bajas de los últimos 6 meses
        var desde = inicioMes.AddMonths(-5);
        var movs = await _context.HistorialColegios.AsNoTracking()
            .Where(h => h.FechaUtc >= desde && (h.Tipo == "Alta" || h.Tipo == "Baja")).Select(h => new { h.Tipo, h.FechaUtc }).ToListAsync();
        var meses = Enumerable.Range(0, 6).Select(i => desde.AddMonths(i)).ToList();

        var actividad = await _context.HistorialColegios.AsNoTracking().OrderByDescending(h => h.FechaUtc).Take(8).ToListAsync();
        var nombres = colegios.ToDictionary(c => c.Id, c => c.Nombre);

        var msgs = await _context.MensajesPlataforma.AsNoTracking().OrderByDescending(m => m.FechaUtc).Take(5).ToListAsync();
        var nuevos = await _context.MensajesPlataforma.CountAsync(m => m.Estado == EstadoMensaje.Nuevo);

        return Json(new
        {
            actualizado = hoy,
            kpi = new
            {
                colegios = colegios.Count,
                activos = activos.Count,
                ingresoMensual = activos.Sum(c => c.PrecioMensual ?? 0),
                porVencer = filas.Count(f => f.estado == "porvencer"),
                vencidos = filas.Count(f => f.estado == "vencido"),
                suspendidos = filas.Count(f => f.estado == "suspendido"),
                bajas = filas.Count(f => f.estado == "baja"),
                altasMes = colegios.Count(c => c.FechaRegistro >= inicioMes),
                mensajesNuevos = nuevos,
                personas = us.Count
            },
            planes = new[]
            {
                new { n = "Psicosocial", v = activos.Count(c => PlanDe(c) == "psicosocial") },
                new { n = "Premium", v = activos.Count(c => PlanDe(c) == "premium") },
                new { n = "Institucional", v = activos.Count(c => PlanDe(c) == "institucional") }
            },
            meses = meses.Select(m => new
            {
                etiqueta = Cl.DateTimeFormat.GetAbbreviatedMonthName(m.Month).TrimEnd('.') + " " + (m.Year % 100),
                altas = movs.Count(x => x.Tipo == "Alta" && x.FechaUtc.Year == m.Year && x.FechaUtc.Month == m.Month),
                bajas = movs.Count(x => x.Tipo == "Baja" && x.FechaUtc.Year == m.Year && x.FechaUtc.Month == m.Month)
            }),
            top = filas.Where(f => f.activo).OrderByDescending(f => f.personas).Take(8).Select(f => new { f.Nombre, f.personas }),
            vencimientos = filas.Where(f => f.activo && f.dias != null && f.dias <= 45).OrderBy(f => f.dias).Take(6)
                .Select(f => new { f.Id, f.Nombre, f.plan, f.dias, f.vence, f.contacto }),
            mensajes = msgs.Select(m => new { m.Id, m.Tipo, m.Nombre, m.ColegioNombre, m.Mensaje, estado = (int)m.Estado, fecha = m.FechaUtc }),
            actividad = actividad.Select(h => new { h.Tipo, h.Detalle, colegio = nombres.TryGetValue(h.ColegioRefId, out var n) ? n : "—", fecha = h.FechaUtc })
        });
    }

    // ---------------- Colegios ----------------
    [HttpGet]
    public async Task<IActionResult> ListaColegios()
    {
        var hoy = DateTime.UtcNow;
        var colegios = await _context.Colegios.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        var us = await _context.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Activo && u.Rol != RolUsuario.AdminPlataforma).Select(u => new { u.ColegioId, u.Rol }).ToListAsync();
        return Json(colegios.Select(c => new
        {
            c.Id,
            c.Nombre,
            c.Rut,
            plan = PlanDe(c),
            planTexto = TextoPlan(PlanDe(c)),
            estado = EstadoDe(c, hoy),
            contacto = c.ContactoNombre,
            correo = c.ContactoCorreo,
            telefono = c.ContactoTelefono,
            registro = c.FechaRegistro,
            vence = c.FechaVencimiento,
            precio = c.PrecioMensual,
            dias = c.FechaVencimiento == null ? (int?)null : (c.FechaVencimiento.Value.Date - hoy.Date).Days,
            funcionarios = us.Count(u => u.ColegioId == c.Id && u.Rol != RolUsuario.Estudiante && u.Rol != RolUsuario.Apoderado),
            alumnos = us.Count(u => u.ColegioId == c.Id && u.Rol == RolUsuario.Estudiante),
            apoderados = us.Count(u => u.ColegioId == c.Id && u.Rol == RolUsuario.Apoderado)
        }));
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        var c = await _context.Colegios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var hoy = DateTime.UtcNow;
        var admins = await _context.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.ColegioId == id && u.Rol == RolUsuario.SuperAdmin && u.Activo).Select(u => u.Email).ToListAsync();
        var hist = await _context.HistorialColegios.AsNoTracking().Where(h => h.ColegioRefId == id)
            .OrderByDescending(h => h.FechaUtc).Take(15).Select(h => new { h.Tipo, h.Detalle, fecha = h.FechaUtc }).ToListAsync();
        return Json(new
        {
            c.Id,
            c.Nombre,
            c.Rut,
            c.Direccion,
            c.Telefono,
            plan = PlanDe(c),
            estado = EstadoDe(c, hoy),
            contacto = c.ContactoNombre,
            correo = c.ContactoCorreo,
            telefonoContacto = c.ContactoTelefono,
            registro = c.FechaRegistro,
            vence = c.FechaVencimiento,
            precio = c.PrecioMensual,
            notas = c.NotasInternas,
            baja = c.FechaBaja,
            motivoBaja = c.MotivoBaja,
            admins,
            historial = hist
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarColegio(int id, string plan, string? contacto, string? correo, string? telefono,
        string? vence, int? precio, string? notas)
    {
        var c = await _context.Colegios.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { error = "Colegio no encontrado." });
        if (precio is < 0 or > 100_000_000) return BadRequest(new { error = "El precio no es válido." });
        if (!string.IsNullOrWhiteSpace(correo) && !Regex.IsMatch(correo.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return BadRequest(new { error = "El correo de contacto no es válido." });

        var antes = PlanDe(c);
        AplicarPlan(c, plan);
        var despues = PlanDe(c);
        if (antes != despues) Log(c.Id, "Plan", $"{TextoPlan(antes)} → {TextoPlan(despues)}");

        var nuevoVence = Fecha(vence);
        if (nuevoVence?.Date != c.FechaVencimiento?.Date)
            Log(c.Id, "Renovación", nuevoVence == null ? "Se quitó la fecha de vencimiento" : "Vencimiento: " + nuevoVence.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture));
        c.FechaVencimiento = nuevoVence;
        c.PrecioMensual = precio;
        c.ContactoNombre = Limpio(contacto, 120);
        c.ContactoCorreo = Limpio(correo, 160)?.ToLowerInvariant();
        c.ContactoTelefono = Limpio(telefono, 40);
        c.NotasInternas = Limpio(notas, 1000);
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extender(int id, int meses)
    {
        if (meses is < 1 or > 36) return BadRequest(new { error = "Meses no válidos." });
        var c = await _context.Colegios.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { error = "Colegio no encontrado." });
        var hoy = DateTime.UtcNow.Date;
        var baseFecha = c.FechaVencimiento != null && c.FechaVencimiento.Value.Date > hoy ? c.FechaVencimiento.Value.Date : hoy;
        c.FechaVencimiento = DateTime.SpecifyKind(baseFecha.AddMonths(meses), DateTimeKind.Utc);
        Log(c.Id, "Renovación", $"+{meses} {(meses == 1 ? "mes" : "meses")} · vence {c.FechaVencimiento:dd-MM-yyyy}");
        await _context.SaveChangesAsync();
        return Json(new { ok = true, vence = c.FechaVencimiento });
    }

    // accion: suspender | baja | reactivar   (nunca se borran datos)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Estado(int id, string accion, string? motivo)
    {
        var c = await _context.Colegios.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { error = "Colegio no encontrado." });
        motivo = Limpio(motivo, 300);
        switch ((accion ?? "").ToLowerInvariant())
        {
            case "suspender": c.Activo = false; Log(c.Id, "Suspensión", motivo ?? "Sin motivo indicado"); break;
            case "baja": c.Activo = false; c.FechaBaja = DateTime.UtcNow; c.MotivoBaja = motivo; Log(c.Id, "Baja", motivo ?? "Sin motivo indicado"); break;
            case "reactivar": c.Activo = true; c.FechaBaja = null; c.MotivoBaja = null; Log(c.Id, "Reactivación", "Colegio reactivado"); break;
            default: return BadRequest(new { error = "Acción no válida." });
        }
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearColegio(string nombre, string? rut, string? telefono, string? direccion, string plan,
        string? contacto, string? correoContacto, string? telefonoContacto, string? vence, int? precio,
        string adminNombre, string adminApellido, string adminRut, string adminCorreo)
    {
        nombre = (nombre ?? "").Trim();
        adminNombre = (adminNombre ?? "").Trim();
        adminApellido = (adminApellido ?? "").Trim();
        adminRut = (adminRut ?? "").Replace(".", "").Replace(" ", "").ToUpperInvariant();
        var correo = (adminCorreo ?? "").Trim().ToLowerInvariant();

        string? error = null;
        if (nombre.Length < 3) error = "Escribe el nombre del colegio.";
        else if (adminNombre.Length == 0 || adminApellido.Length == 0) error = "Escribe el nombre y apellido de quien administrará el colegio.";
        else if (!Regex.IsMatch(adminRut, @"^[0-9]+-[0-9K]$")) error = "El RUT del administrador/a no es válido (formato 12345678-9).";
        else if (!Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) error = "El correo del administrador/a no es válido.";
        else if (precio is < 0 or > 100_000_000) error = "El precio no es válido.";
        else if (await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == correo)) error = "Ese correo ya está registrado en AUKA.";
        if (error != null) return BadRequest(new { error });

        var c = new Colegio
        {
            Nombre = nombre,
            Rut = (rut ?? "").Trim(),
            Telefono = (telefono ?? "").Trim(),
            Direccion = (direccion ?? "").Trim(),
            Activo = true,
            ContactoNombre = Limpio(contacto, 120),
            ContactoCorreo = Limpio(correoContacto, 160)?.ToLowerInvariant(),
            ContactoTelefono = Limpio(telefonoContacto, 40),
            FechaRegistro = DateTime.UtcNow,
            FechaVencimiento = Fecha(vence),
            PrecioMensual = precio
        };
        AplicarPlan(c, plan);
        _context.Colegios.Add(c);
        await _context.SaveChangesAsync();

        var clave = GenerarClave();
        var u = new Usuario
        {
            ColegioId = c.Id,
            Rut = adminRut,
            Nombre = adminNombre,
            Apellido = adminApellido,
            Email = correo,
            Rol = RolUsuario.SuperAdmin,
            CargoInstitucional = "Administrador/a del colegio",
            Activo = true,
            DebeCambiarPassword = true,
            FechaCreacion = DateTime.UtcNow
        };
        u.PasswordHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        _context.Usuarios.Add(u);
        Log(c.Id, "Alta", $"{TextoPlan(PlanDe(c))} · administrador/a {u.Email}");
        await _context.SaveChangesAsync();

        return Json(new { ok = true, colegio = c.Nombre, numero = c.Id, correo = u.Email, clave });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestablecerClaveAdmin(int colegioId, string correo)
    {
        correo = (correo ?? "").Trim().ToLowerInvariant();
        var u = await _context.Usuarios.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ColegioId == colegioId && x.Rol == RolUsuario.SuperAdmin && x.Email.ToLower() == correo);
        if (u == null) return NotFound(new { error = "No encontré a ese administrador/a en el colegio." });
        var clave = GenerarClave();
        u.PasswordHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        u.DebeCambiarPassword = true;
        Log(colegioId, "Datos", "Nueva clave provisoria para " + u.Email);
        await _context.SaveChangesAsync();
        return Json(new { ok = true, correo = u.Email, clave });
    }

    // ---------------- Mensajes ----------------
    [HttpGet]
    public async Task<IActionResult> ListaMensajes()
    {
        var l = await _context.MensajesPlataforma.AsNoTracking().OrderByDescending(m => m.FechaUtc).Take(300).ToListAsync();
        return Json(l.Select(m => new
        {
            m.Id,
            m.Tipo,
            m.Nombre,
            m.Cargo,
            m.ColegioNombre,
            m.Correo,
            m.Telefono,
            m.Mensaje,
            estado = (int)m.Estado,
            nota = m.NotaInterna,
            fecha = m.FechaUtc
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MensajeActualizar(int id, int estado, string? nota)
    {
        var m = await _context.MensajesPlataforma.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound(new { error = "Mensaje no encontrado." });
        if (!Enum.IsDefined(typeof(EstadoMensaje), estado)) return BadRequest(new { error = "Estado no válido." });
        m.Estado = (EstadoMensaje)estado;
        m.NotaInterna = Limpio(nota, 1000);
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }
}
