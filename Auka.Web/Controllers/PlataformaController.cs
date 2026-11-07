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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Auka.Application.Interfaces;
using Auka.Web.Services;

namespace Auka.Web.Controllers;

/// <summary>
/// Panel del dueño de la plataforma (AdminPlataforma): suscripciones, planes, vencimientos, contactos y mensajes.
/// Privacidad: aquí solo hay datos comerciales y CANTIDADES. Nunca fichas, bitácoras, anotaciones ni citaciones.
/// </summary>
[Authorize(Roles = "AdminPlataforma")]
public class PlataformaController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ICorreoService _correo;
    private readonly CorreoOptions _correoOpt;
    public PlataformaController(ApplicationDbContext context, IMemoryCache cache, ICorreoService correo, IOptions<CorreoOptions> correoOpt)
    {
        _context = context;
        _cache = cache;
        _correo = correo;
        _correoOpt = correoOpt.Value;
    }

    private static string TipoValido(string? tipo) => Organizacion.Tipos.Contains(tipo ?? "") ? tipo! : "Colegio";

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
    [HttpGet] public IActionResult Correos() => View();

    // ---------------- Correos: ¿están saliendo? ----------------
    // Solo estado técnico del envío (organización, destino abreviado, estado y error). Nunca el contenido del aviso.
    private static string Abreviar(string? correo)
    {
        var c = (correo ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var i = c.IndexOf('@');
        return i <= 0 ? c : (c[..Math.Min(3, i)] + "***" + c[i..]);
    }

    [HttpGet]
    public async Task<IActionResult> EstadoCorreos()
    {
        var desde = DateTime.UtcNow.AddDays(-7);
        var ult = await _context.Notificaciones.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(n => n.Id).Take(40)
            .Select(n => new { n.Id, n.ColegioId, n.CreadaEnUtc, n.CorreoDestino, n.EstadoCorreo, n.UltimoErrorCorreo, n.IntentosCorreo })
            .ToListAsync();
        var conteo = await _context.Notificaciones.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.CreadaEnUtc >= desde).GroupBy(n => n.EstadoCorreo)
            .Select(g => new { e = g.Key, n = g.Count() }).ToListAsync();
        var ids = ult.Select(x => x.ColegioId).Distinct().ToList();
        var orgs = await _context.Colegios.IgnoreQueryFilters().AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nombre);
        var (url, origen) = UrlPublica.Diagnostico(HttpContext, _correoOpt.UrlBase);
        int Cant(EstadoEnvioCorreo e) => conteo.Where(x => x.e == e).Sum(x => x.n);
        return Json(new
        {
            cfg = new
            {
                habilitado = _correoOpt.Habilitado,
                servidor = $"{_correoOpt.Servidor}:{_correoOpt.Puerto}",
                usuario = _correoOpt.Usuario,
                clave = !string.IsNullOrWhiteSpace(_correoOpt.Clave),
                url,
                origen
            },
            semana = new { enviados = Cant(EstadoEnvioCorreo.Enviado), fallidos = Cant(EstadoEnvioCorreo.Fallido), simulados = Cant(EstadoEnvioCorreo.Simulado), sinCorreo = Cant(EstadoEnvioCorreo.SinCorreo) },
            ultimos = ult.Select(x => new
            {
                f = x.CreadaEnUtc.ToLocalTime().ToString("dd-MM HH:mm", Cl),
                org = orgs.GetValueOrDefault(x.ColegioId, "—"),
                d = Abreviar(x.CorreoDestino),
                e = (int)x.EstadoCorreo,
                err = x.UltimoErrorCorreo ?? "",
                i = x.IntentosCorreo
            })
        });
    }

    // POST: manda un correo de prueba y devuelve el resultado tal cual (para saber al tiro si Gmail acepta la clave)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProbarCorreo(string destino)
    {
        destino = (destino ?? "").Trim();
        if (!Regex.IsMatch(destino, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return BadRequest(new { error = "Escribe un correo válido." });
        var url = UrlPublica.Obtener(HttpContext, _correoOpt.UrlBase);
        var html = CorreoPlantilla.Construir("Correo de prueba de AUKA",
            "Si lees esto, el envío de correos de AUKA funciona.\n\nEl botón de abajo debe abrir AUKA desde cualquier lugar (no solo desde tu PC).",
            url + "/", "AUKA");
        var r = await _correo.EnviarAsync(new[] { destino }, "AUKA · Correo de prueba", html);
        return Json(new { ok = r.Ok, simulado = r.Simulado, error = r.Error, url });
    }

    // POST: vuelve a intentar ahora los correos que fallaron (por ejemplo, después de arreglar la clave de Gmail)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReintentarCorreos()
    {
        var fallidos = await _context.Notificaciones.IgnoreQueryFilters()
            .Where(n => n.EstadoCorreo == EstadoEnvioCorreo.Fallido && n.CreadaEnUtc >= DateTime.UtcNow.AddDays(-3))
            .OrderBy(n => n.Id).Take(50).ToListAsync();
        var raiz = UrlPublica.Obtener(HttpContext, _correoOpt.UrlBase);
        int ok = 0, mal = 0; string? ultimo = null;
        foreach (var n in fallidos)
        {
            var destinos = (n.CorreoDestino ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            var url = string.IsNullOrEmpty(n.Enlace) ? null : raiz + n.Enlace;
            var r = await _correo.EnviarAsync(destinos, n.Titulo, CorreoPlantilla.Construir(n.Titulo, n.Mensaje, url, ""));
            n.IntentosCorreo++;
            if (r.Ok) { n.EstadoCorreo = r.Simulado ? EstadoEnvioCorreo.Simulado : EstadoEnvioCorreo.Enviado; n.UltimoErrorCorreo = null; ok++; }
            else { n.UltimoErrorCorreo = r.Error != null && r.Error.Length > 300 ? r.Error[..300] : r.Error; ultimo = n.UltimoErrorCorreo; mal++; }
        }
        await _context.SaveChangesAsync();
        return Json(new { ok, mal, error = ultimo, total = fallidos.Count });
    }

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
            tipo = c.TipoOrganizacion,
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
            tipo = c.TipoOrganizacion,
            planillas = Organizacion.Activas(c.PlanillasActivas),
            calCompartido = c.CalendarioCompartido,
            bitCompartida = c.BitacoraCompartida,
            psicoAdmin = c.PsicosocialAdministrador,
            porArea = Organizacion.PorArea(c.FichasPorArea),
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
        string? vence, int? precio, string? notas, string? tipo, string? planillas,
        bool calCompartido = true, bool bitCompartida = true, string? porArea = null, bool psicoAdmin = false)
    {
        var c = await _context.Colegios.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { error = "Colegio no encontrado." });
        if (precio is < 0 or > 100_000_000) return BadRequest(new { error = "El precio no es válido." });
        if (!string.IsNullOrWhiteSpace(correo) && !Regex.IsMatch(correo.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return BadRequest(new { error = "El correo de contacto no es válido." });

        // Empresa, fundación o particular: solo Plan Psicosocial
        if (!Organizacion.PermitePremium(TipoValido(tipo))) plan = "psicosocial";

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

        var nuevoTipo = TipoValido(tipo);
        if (nuevoTipo != c.TipoOrganizacion) Log(c.Id, "Datos", $"Tipo: {c.TipoOrganizacion} → {nuevoTipo}");
        c.TipoOrganizacion = nuevoTipo;
        if (Organizacion.Activas(planillas).Length == 0) return BadRequest(new { error = "Activa al menos una planilla." });
        var nuevasPlanillas = Organizacion.Normalizar(planillas);
        if (nuevasPlanillas != c.PlanillasActivas)
            Log(c.Id, "Datos", "Planillas: " + string.Join(", ", Organizacion.Activas(nuevasPlanillas).Select(k => Organizacion.Planillas[k])));
        c.PlanillasActivas = nuevasPlanillas;

        // Cómo trabaja el equipo psicosocial: compartido o separado
        var nuevoPorArea = Organizacion.NormalizarPorArea(porArea);
        if (c.CalendarioCompartido != calCompartido || c.BitacoraCompartida != bitCompartida || c.FichasPorArea != nuevoPorArea)
            Log(c.Id, "Datos", $"Equipo: calendario {(calCompartido ? "compartido" : "separado")}, bitácora {(bitCompartida ? "compartida" : "separada")}, " +
                $"fichas por área: {(nuevoPorArea == null ? "ninguna" : string.Join(", ", Organizacion.PorArea(nuevoPorArea).Select(k => Organizacion.NombreFichaSeparable[k])))}");
        c.CalendarioCompartido = calCompartido;
        c.BitacoraCompartida = bitCompartida;
        c.FichasPorArea = nuevoPorArea;

        // Plan Premium: ¿el administrador del colegio ve el resumen psicosocial? (nunca las fichas)
        if (c.PsicosocialAdministrador != psicoAdmin)
            Log(c.Id, "Datos", psicoAdmin ? "Resumen psicosocial activado para el administrador/a" : "Resumen psicosocial desactivado para el administrador/a");
        c.PsicosocialAdministrador = psicoAdmin;

        await _context.SaveChangesAsync();
        _cache.Remove(Organizacion.LlaveCache(c.Id));   // la organización ve el cambio al tiro
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
        string adminNombre, string adminApellido, string adminRut, string adminCorreo, string? tipo = null, string? planillas = null)
    {
        nombre = (nombre ?? "").Trim();
        adminNombre = (adminNombre ?? "").Trim();
        adminApellido = (adminApellido ?? "").Trim();
        adminRut = (adminRut ?? "").Replace(".", "").Replace(" ", "").ToUpperInvariant();
        var correo = (adminCorreo ?? "").Trim().ToLowerInvariant();

        string? error = null;
        if (nombre.Length < 3) error = "Escribe el nombre de la organización.";
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
            PrecioMensual = precio,
            TipoOrganizacion = TipoValido(tipo)
        };
        // Un colegio parte con todas las planillas; otra organización, con las básicas (si no se eligieron)
        c.PlanillasActivas = Organizacion.Normalizar(string.IsNullOrWhiteSpace(planillas) && c.TipoOrganizacion != "Colegio"
            ? Organizacion.PlanillasPorDefectoOrganizacion : planillas);
        AplicarPlan(c, Organizacion.PermitePremium(c.TipoOrganizacion) ? plan : "psicosocial");
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
            CargoInstitucional = c.TipoOrganizacion == "Colegio" ? "Administrador/a del colegio" : "Administrador/a de la organización",
            Activo = true,
            DebeCambiarPassword = true,
            FechaCreacion = DateTime.UtcNow
        };
        u.PasswordHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        _context.Usuarios.Add(u);
        Log(c.Id, "Alta", $"{c.TipoOrganizacion} · {TextoPlan(PlanDe(c))} · administrador/a {u.Email}");
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
