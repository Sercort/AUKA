using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Models;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Auka.Web.Controllers;

[Authorize(Policy = "AccesoPsicosocial")]
public class PsychosocialController : Controller
{
    // El administrador del Plan Psicosocial solo gestiona alumnos/as: nunca ve fichas, bitácoras ni calendario clínico
    private static readonly HashSet<string> AccionesAdministrador = new() { "Alumnos", "ImportarAlumnos", "AgregarAlumnos", "RegenerarClavesAlumnos" };
    private bool EsAdministrador => User.FindFirst(ClaimTypes.Role)?.Value == "SuperAdmin";

    // La orientadora NO ve fichas confidenciales, bitácoras ni datos de las cuentas. Ve el panel, el listado de
    // alumnos/as y el calendario del equipo: lo de los demás solo lo mira; puede anotar y citar lo suyo.
    private static readonly HashSet<string> AccionesOrientadora = new()
    {
        "Index", "Resumen", "Alumnos", "Anotaciones", "CrearAnotacion",
        "EventosCalendario", "GuardarEvento", "MarcarEvento", "EliminarEvento",
        "CrearCitacion", "Equipo", "Solicitudes", "AtenderCitacion",
        "Registros", "GuardarRegistro", "EditarRegistro", "EliminarRegistro",
        "ListaCursos", "CambiarCurso", "Contacto",
        "AgregarApoderado", "QuitarApoderado", "AccesoApoderado"
    };
    // Fichas que registra la orientadora (las ve todo el equipo; no son confidenciales)
    private static readonly HashSet<string> TiposOrientacion = new() { "ori", "ento", "tal", "comp" };
    private bool EsOrientadora => User.IsInRole("Orientadora");

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (EsOrientadora)
        {
            var accion = context.ActionDescriptor.RouteValues["action"] ?? "";
            if (!AccionesOrientadora.Contains(accion))
            {
                context.Result = StatusCode(403, new { error = "La orientadora no tiene acceso a fichas confidenciales." });
                return;
            }
        }
        if (EsAdministrador)
        {
            var accion = context.ActionDescriptor.RouteValues["action"] ?? "";
            if (!AccionesAdministrador.Contains(accion))
            {
                context.Result = accion == "Index" ? RedirectToAction("Index", "Administracion") : StatusCode(403);
                return;
            }
        }
        base.OnActionExecuting(context);
    }

    private static readonly HashSet<string> TiposValidos = new()
    {
        "Citación", "Taller", "Consejo", "Coordinación", "Reunión", "Institucional", "Feriado"
    };

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly INotificacionService _notif;

    private readonly IAccesoService _acceso;

    public PsychosocialController(ApplicationDbContext context, IMemoryCache cache, INotificacionService notif, IAccesoService acceso)
    {
        _context = context;
        _cache = cache;
        _notif = notif;
        _acceso = acceso;
    }

    // Tipo de organización de la sesión: colegio (alumnos/as, cursos) u otra (pacientes, sin curso)
    private Task<DatosOrganizacion> OrgAsync() => OrganizacionFilter.ObtenerAsync(_context, _cache);

    // Fichas que este usuario no puede ver (separadas por área y de la otra área)
    private async Task<string[]> FichasOcultasAsync() => Organizacion.FichasOcultas(await OrgAsync(), User);

    // Calendario y bitácora: compartidos por todo el equipo o cada profesional lo suyo
    private async Task<bool> CalendarioPropioAsync() => !(await OrgAsync()).CalendarioCompartido;
    private async Task<bool> BitacoraPropiaAsync() => !(await OrgAsync()).BitacoraCompartida;

    // GET: /Psychosocial
    [HttpGet]
    public IActionResult Index()
    {
        var modelo = new PsychosocialDashboardViewModel
        {
            NombreProfesional = User.Identity?.Name ?? "Profesional"
        };

        return View(modelo);
    }

    // GET: /Psychosocial/Resumen -> números reales del colegio (vacío si el colegio es nuevo)
    [HttpGet]
    public async Task<IActionResult> Resumen()
    {
        var alumnos = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null)
            .Select(e => new { e.Curso!.Nombre, e.Curso.Letra })
            .ToListAsync();

        var porCurso = alumnos
            .GroupBy(a => string.IsNullOrEmpty(a.Letra) ? a.Nombre : a.Nombre + " " + a.Letra)
            .Select(g => new { c = g.Key, n = g.Count() })
            .ToList();

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        int dow = ((int)hoy.DayOfWeek + 6) % 7; // lunes = 0
        var lunes = hoy.AddDays(-dow);
        var viernes = lunes.AddDays(4);
        var semana = await _context.Citaciones.AsNoTracking()
            .Where(c => c.Fecha >= lunes && c.Fecha <= viernes && c.Estado != EstadoCitacion.Cancelada)
            .Select(c => c.Estado).ToListAsync();

        var sinConfirmar = await _context.Citaciones.AsNoTracking()
            .Include(c => c.Estudiante).ThenInclude(e => e!.Usuario)
            .Where(c => c.Fecha >= hoy && c.Estado == EstadoCitacion.Pendiente)
            .OrderBy(c => c.Fecha).ThenBy(c => c.Hora).Take(5).ToListAsync();

        // Citaciones cuya fecha ya pasó y nadie marcó si asistió
        var porMarcar = await _context.Citaciones.AsNoTracking()
            .CountAsync(c => c.Fecha < hoy && (c.Estado == EstadoCitacion.Pendiente || c.Estado == EstadoCitacion.Confirmada));

        // Inactivos: su última asistencia fue hace más de DiasInactividad días y no tienen una cita futura
        var limite = hoy.AddDays(-DiasInactividad);
        var ultimas = await _context.Citaciones.AsNoTracking()
            .Where(c => c.Estado == EstadoCitacion.Realizada)
            .GroupBy(c => c.EstudianteId)
            .Select(g => new { id = g.Key, ultima = g.Max(c => c.Fecha) })
            .Where(x => x.ultima < limite)
            .ToListAsync();
        var conCitaFutura = (await _context.Citaciones.AsNoTracking()
            .Where(c => c.Fecha >= hoy && c.Estado != EstadoCitacion.Cancelada)
            .Select(c => c.EstudianteId).Distinct().ToListAsync()).ToHashSet();
        var idsInactivos = ultimas.Where(x => !conCitaFutura.Contains(x.id)).OrderBy(x => x.ultima).Take(8).ToList();
        var idsLista = idsInactivos.Select(x => x.id).ToList();
        var nombresIn = await _context.Estudiantes.AsNoTracking().Include(e => e.Usuario)
            .Where(e => idsLista.Contains(e.Id) && e.Usuario != null && e.Usuario.Activo)
            .ToDictionaryAsync(e => e.Id, e => $"{e.Usuario!.Nombre} {e.Usuario.Apellido}".Trim());

        return Json(new
        {
            alumnos = alumnos.Count,
            cursos = porCurso.Count,
            porMarcar,
            diasInactividad = DiasInactividad,
            inactivos = idsInactivos.Where(x => nombresIn.ContainsKey(x.id))
                .Select(x => new { id = x.id, n = nombresIn[x.id], d = hoy.DayNumber - x.ultima.DayNumber }),
            porCurso,
            citasSemana = semana.Count,
            citasSinConfirmar = semana.Count(x => x == EstadoCitacion.Pendiente),
            proximasCitas = sinConfirmar.Select(c => new
            {
                f = c.Fecha.ToString("dd-MM", CultureInfo.InvariantCulture),
                h = c.Hora.HasValue ? c.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
                x = (c.Estudiante?.Usuario != null ? $"{c.Estudiante.Usuario.Nombre} {c.Estudiante.Usuario.Apellido}".Trim() : "Alumno/a") + " · " + c.CitadoA
            })
        });
    }

    // ---------------------------------------------------------------
    // ANOTACIONES DE CONDUCTA (el mismo libro que usan profesores e inspectores)
    // Las ven profesores/as y apoderados/as; no son confidenciales como la bitácora.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/Anotaciones?estudianteId=5
    [HttpGet]
    public async Task<IActionResult> Anotaciones(int estudianteId)
    {
        // Estudiantes tiene filtro multitenant: si el alumno/a es de otro colegio, no existe para esta sesión
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound();

        var lista = await _context.Anotaciones.AsNoTracking()
            .Include(a => a.Docente)
            .Where(a => a.EstudianteId == estudianteId)
            .OrderByDescending(a => a.Fecha)
            .Take(30)
            .ToListAsync();

        return Json(lista.Select(a => new
        {
            id = a.Id,
            t = (int)a.Tipo,
            d = a.Detalle,
            f = a.Fecha.ToString("o", CultureInfo.InvariantCulture),
            a = a.Docente != null ? $"{a.Docente.Nombre} {a.Docente.Apellido}".Trim() : ""
        }));
    }

    // POST: /Psychosocial/CrearAnotacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearAnotacion(int estudianteId, TipoAnotacion tipo, string? detalle)
    {
        detalle = (detalle ?? "").Trim();
        if (detalle.Length < 3) return BadRequest(new { error = "Escribe el detalle de la anotación." });
        if (detalle.Length > 500) return BadRequest(new { error = "El detalle es muy largo (máximo 500 caracteres)." });
        if (!Enum.IsDefined(tipo)) return BadRequest(new { error = "Tipo de anotación no válido." });
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound(new { error = "Alumno/a no encontrado." });

        _context.Anotaciones.Add(new Anotacion
        {
            EstudianteId = estudianteId,
            DocenteId = UsuarioActualId(),
            Tipo = tipo,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // ---------------------------------------------------------------
    // CITACIONES (se guardan, quedan en el calendario y avisan por correo y por la plataforma)
    // ---------------------------------------------------------------

    private static readonly string[] CitadosValidos = { "Alumno", "Apoderado", "Alumno + apoderado", "Profesor jefe", "Paciente" };

    // ---------------------------------------------------------------
    // SOLICITUDES Y RESPUESTAS: citas que pidió el alumno/a o el apoderado/a, y respuestas a citaciones
    // (rechazadas o con otra fecha propuesta). Cada profesional ve las que le tocan.
    // ---------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Solicitudes()
    {
        var yo = UsuarioActualId();
        var marca = "," + yo + ",";
        var mia = Citaciones.DestinatarioDe(User);
        var desde = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var lista = await _context.Citaciones.AsNoTracking()
            .Include(c => c.Estudiante).ThenInclude(e => e!.Usuario)
            .Include(c => c.Estudiante).ThenInclude(e => e!.Curso)
            .Where(c => (c.Estado == EstadoCitacion.Solicitada && c.Destinatario == mia) ||
                        ((c.Estado == EstadoCitacion.OtraFecha || c.Estado == EstadoCitacion.Rechazada) && c.Fecha >= desde &&
                         (c.CreadaPorId == yo || (c.Participantes != null && ("," + c.Participantes + ",").Contains(marca)))))
            .OrderBy(c => c.Fecha).Take(50).ToListAsync();
        var esColegio = (await OrgAsync()).V.EsColegio;
        return Json(lista.Select(c => new
        {
            id = c.Id,
            eid = c.EstudianteId,
            n = c.Estudiante?.Usuario == null ? "" : $"{c.Estudiante.Usuario.Nombre} {c.Estudiante.Usuario.Apellido}".Trim(),
            cu = esColegio ? NombreCurso(c.Estudiante?.Curso) : "",
            e = (int)c.Estado,
            est = Citaciones.Estado(c.Estado),
            a = c.CitadoA,
            f = c.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            h = c.Hora.HasValue ? c.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            fp = c.FechaPropuesta.HasValue ? c.FechaPropuesta.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
            hp = c.HoraPropuesta.HasValue ? c.HoraPropuesta.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            m = c.Motivo,
            r = c.Respuesta ?? "",
            d = c.Destinatario ?? ""
        }));
    }

    // POST: el equipo atiende una solicitud o una respuesta
    //   agendar  -> solicitud: queda agendada (si cambia la fecha pedida, la persona debe confirmar)
    //   aceptar  -> otra fecha propuesta: se acepta y queda confirmada
    //   mantener -> otra fecha propuesta: se mantiene la original y se pide confirmar de nuevo
    //   cerrar   -> rechazada o solicitud que no procede: se cancela (con comentario)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtenderCitacion(int id, string accion, string? fecha, string? hora, string? comentario)
    {
        var c = await _context.Citaciones.Include(x => x.Estudiante).ThenInclude(e => e!.Usuario).FirstOrDefaultAsync(x => x.Id == id);
        if (c?.Estudiante?.Usuario == null) return NotFound();
        var yo = UsuarioActualId();
        var esMia = c.Estado == EstadoCitacion.Solicitada
            ? c.Destinatario == Citaciones.DestinatarioDe(User)
            : c.CreadaPorId == yo || Citaciones.Participantes(c.Participantes).Contains(yo);
        if (!esMia) return StatusCode(403, new { error = "Esta citación la atiende otro profesional." });
        comentario = (comentario ?? "").Trim();
        if (comentario.Length > 300) return BadRequest(new { error = "El comentario es muy largo (máximo 300 caracteres)." });

        var nombre = $"{c.Estudiante.Usuario.Nombre} {c.Estudiante.Usuario.Apellido}".Trim();
        string Cuando(DateOnly fx, TimeOnly? hx) => fx.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) + (hx.HasValue ? " a las " + hx.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "");
        string titulo, mensaje;

        if (accion == "agendar" && c.Estado == EstadoCitacion.Solicitada)
        {
            if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) || f < DateOnly.FromDateTime(DateTime.Today))
                return BadRequest(new { error = "Elige una fecha desde hoy." });
            TimeOnly? h = TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hh) ? hh : null;
            var igual = f == c.Fecha && (h == null || h == c.Hora);
            c.Fecha = f; c.Hora = h ?? c.Hora;
            c.Estado = igual ? EstadoCitacion.Confirmada : EstadoCitacion.Pendiente;
            c.CreadaPorId = yo;   // desde ahora la lleva quien la agendó (le llegan las respuestas)
            var ev = new EventoCalendario { Fecha = c.Fecha, Hora = c.Hora, Tipo = "Citación", Titulo = $"Cita pedida ({c.CitadoA.ToLower()}): {nombre}", Responsable = "Equipo psicosocial", CreadoPorId = yo };
            _context.EventosCalendario.Add(ev);
            await _context.SaveChangesAsync();
            c.EventoCalendarioId = ev.Id;
            titulo = "Tu solicitud de cita fue agendada";
            mensaje = $"La cita que pediste quedó para el {Cuando(c.Fecha, c.Hora)}." + (igual ? "" : " Como cambió la fecha que pediste, confírmala en AUKA.") + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "");
        }
        else if (accion == "aceptar" && c.Estado == EstadoCitacion.OtraFecha && c.FechaPropuesta.HasValue)
        {
            c.Fecha = c.FechaPropuesta.Value; c.Hora = c.HoraPropuesta ?? c.Hora;
            c.Estado = EstadoCitacion.Confirmada;
            if (c.EventoCalendarioId != null)
            {
                var ev = await _context.EventosCalendario.FirstOrDefaultAsync(e => e.Id == c.EventoCalendarioId);
                if (ev != null) { ev.Fecha = c.Fecha; ev.Hora = c.Hora; ev.ModificadoPorId = yo; }
            }
            titulo = "Se aceptó la nueva fecha";
            mensaje = $"La citación de {nombre} queda para el {Cuando(c.Fecha, c.Hora)}." + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "");
        }
        else if (accion == "mantener" && c.Estado == EstadoCitacion.OtraFecha)
        {
            c.Estado = EstadoCitacion.Pendiente;
            titulo = "Se mantiene la fecha de la citación";
            mensaje = $"La citación de {nombre} se mantiene el {Cuando(c.Fecha, c.Hora)}. Por favor confírmala en AUKA." + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "");
        }
        else if (accion == "cerrar" && c.Estado is EstadoCitacion.Solicitada or EstadoCitacion.Rechazada or EstadoCitacion.OtraFecha)
        {
            var eraSolicitud = c.Estado == EstadoCitacion.Solicitada;
            c.Estado = EstadoCitacion.Cancelada;
            if (c.EventoCalendarioId != null)
            {
                var ev = await _context.EventosCalendario.FirstOrDefaultAsync(e => e.Id == c.EventoCalendarioId);
                if (ev != null) _context.EventosCalendario.Remove(ev);
            }
            titulo = eraSolicitud ? "Tu solicitud de cita" : "Citación cancelada";
            mensaje = eraSolicitud
                ? "Por ahora no se pudo agendar la cita que pediste." + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "")
                : $"La citación de {nombre} del {Cuando(c.Fecha, c.Hora)} quedó cancelada." + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "");
            if (!eraSolicitud) { await _context.SaveChangesAsync(); return Json(new { ok = true }); }   // ya avisó quien rechazó
        }
        else return BadRequest(new { error = "Esa acción no corresponde al estado de la citación." });

        await _context.SaveChangesAsync();

        // Aviso a quien fue citado/a (o a quien pidió la cita)
        var avisar = new List<int>();
        if (c.SolicitadaPorId.HasValue) avisar.Add(c.SolicitadaPorId.Value);
        else
        {
            var aps = !c.Estudiante.AccesoPropio || c.CitadoA.Contains("Apoderado");
            if ((c.CitadoA.Contains("Alumno") || c.CitadoA == "Paciente") && c.Estudiante.AccesoPropio) avisar.Add(c.Estudiante.UsuarioId);
            if (aps)
                avisar.AddRange(await _context.EstudiantesApoderados.Where(x => x.EstudianteId == c.EstudianteId && x.Apoderado != null)
                    .Select(x => x.Apoderado!.UsuarioId).ToListAsync());
        }
        try { await _notif.NotificarAsync(avisar.Distinct(), titulo, mensaje, "/Portafolio?estudianteId=" + c.EstudianteId); } catch { }
        return Json(new { ok = true });
    }

    // ---------------------------------------------------------------
    // EQUIPO: para elegir quién participa en una citación y quién ve cada nota del calendario
    // ---------------------------------------------------------------
    private static readonly RolUsuario[] RolesEquipo = { RolUsuario.Psicologo, RolUsuario.Psicopedagogo, RolUsuario.TrabajadorSocial, RolUsuario.Orientadora };

    private async Task<List<(int Id, string Nombre, RolUsuario Rol)>> EquipoAsync()
    {
        var tenant = _context.CurrentTenantId;
        var l = await _context.Usuarios.AsNoTracking()
            .Where(u => u.ColegioId == tenant && u.Activo && RolesEquipo.Contains(u.Rol))
            .OrderBy(u => u.Nombre).Select(u => new { u.Id, u.Nombre, u.Apellido, u.Rol }).ToListAsync();
        return l.Select(u => (u.Id, $"{u.Nombre} {u.Apellido}".Trim(), u.Rol)).ToList();
    }

    private static string NombreRol(RolUsuario r) => r switch
    {
        RolUsuario.Psicologo => "Psicólogo/a",
        RolUsuario.Psicopedagogo => "Psicopedagogo/a",
        RolUsuario.TrabajadorSocial => "Trabajador/a social",
        RolUsuario.Orientadora => "Orientadora",
        _ => ""
    };

    // GET: /Psychosocial/Equipo -> los demás profesionales del equipo psicosocial
    [HttpGet]
    public async Task<IActionResult> Equipo()
    {
        var yo = UsuarioActualId();
        return Json((await EquipoAsync()).Where(u => u.Id != yo).Select(u => new { id = u.Id, n = u.Nombre, rol = NombreRol(u.Rol) }));
    }

    // Deja solo ids del equipo de este colegio; "" o null = todo el equipo. Quien crea siempre queda incluido.
    private async Task<string?> VisibleParaValidoAsync(string? csv, int creador)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var validos = (await EquipoAsync()).Select(u => u.Id).ToHashSet();
        var ids = Citaciones.Participantes(csv).Where(validos.Contains).Append(creador).Distinct().ToList();
        return string.Join(",", ids);
    }

    // POST: /Psychosocial/CrearCitacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearCitacion(int estudianteId, string citadoA, string fecha, string? hora, string? motivo,
        string? participantes = null, bool soloParticipantes = false, bool informarOtro = false)
    {
        citadoA = (citadoA ?? "").Trim();
        motivo = (motivo ?? "").Trim();
        if (!CitadosValidos.Contains(citadoA)) return BadRequest(new { error = "Elige a quién se cita." });
        if (motivo.Length < 3) return BadRequest(new { error = "Escribe el motivo de la citación." });
        if (motivo.Length > 300) return BadRequest(new { error = "El motivo es muy largo (máximo 300 caracteres)." });
        if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return BadRequest(new { error = "Fecha inválida." });
        if (f < DateOnly.FromDateTime(DateTime.Today)) return BadRequest(new { error = "La fecha no puede ser anterior a hoy." });
        TimeOnly? h = null;
        if (!string.IsNullOrWhiteSpace(hora))
        {
            if (!TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hh))
                return BadRequest(new { error = "Hora inválida." });
            h = hh;
        }

        // Estudiantes tiene filtro multitenant: un alumno/a de otro colegio no existe para esta sesión
        var est = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
            .FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null) return NotFound(new { error = "Alumno/a no encontrado." });
        var nombre = $"{est.Usuario.Nombre} {est.Usuario.Apellido}".Trim();
        var yoCita = UsuarioActualId();
        var equipoValido = (await EquipoAsync()).Select(u => u.Id).ToHashSet();
        var otros = Citaciones.Participantes(participantes).Where(id => equipoValido.Contains(id) && id != yoCita).ToList();

        var evento = new EventoCalendario
        {
            Fecha = f,
            Hora = h,
            Tipo = "Citación",
            Titulo = $"Citación ({citadoA.ToLower()}): {nombre}",
            Responsable = "Equipo psicosocial",
            Realizada = false,
            CreadoPorId = yoCita,
            // "Solo los que participan": la ven quien cita y los profesionales marcados
            VisiblePara = soloParticipantes ? string.Join(",", otros.Append(yoCita)) : null
        };
        _context.EventosCalendario.Add(evento);
        await _context.SaveChangesAsync();

        var cit = new Citacion
        {
            EstudianteId = est.Id,
            CitadoA = citadoA,
            Fecha = f,
            Hora = h,
            Motivo = motivo,
            CreadaPorId = yoCita,
            EventoCalendarioId = evento.Id,
            Participantes = otros.Count > 0 ? string.Join(",", otros) : null,
            InformarOtro = informarOtro
        };
        _context.Citaciones.Add(cit);
        await _context.SaveChangesAsync();

        // A quién se avisa
        var ids = new List<int>();
        var apoderados = await _context.EstudiantesApoderados.Where(x => x.EstudianteId == est.Id && x.Apoderado != null)
            .Select(x => x.Apoderado!.UsuarioId).ToListAsync();
        if (citadoA.Contains("Alumno") || citadoA == "Paciente")
        {
            // Alumno/a sin cuenta propia (kínder a 6° básico): la citación le llega y la confirma su apoderado/a
            if (est.AccesoPropio) ids.Add(est.UsuarioId); else ids.AddRange(apoderados);
        }
        if (citadoA.Contains("Apoderado")) ids.AddRange(apoderados);
        ids = ids.Distinct().ToList();
        if (citadoA == "Profesor jefe" && est.Curso?.ProfesorJefeId != null) ids.Add(est.Curso.ProfesorJefeId.Value);

        var cuando = f.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) + (h.HasValue ? " a las " + h.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "");
        var lugar = (await OrgAsync()).V.EsColegio ? "en el colegio" : "";
        var mensaje = $"Se solicita su presencia {lugar} el {cuando} para una citación con el equipo psicosocial, por {nombre}.\nMotivo: {motivo}\nPuede confirmar su asistencia desde AUKA.";
        var r = await _notif.NotificarAsync(ids, "Citación del equipo psicosocial", mensaje, "/Portafolio?estudianteId=" + est.Id);

        // Solo se avisa a quien corresponde: los citados (arriba), los profesionales que participan y,
        // si se cita solo al alumno/a, su apoderado/a queda informado/a (pero confirma el alumno/a).
        try
        {
            if (otros.Count > 0)
                await _notif.NotificarAsync(otros, "Te sumaron a una citación",
                    $"Participas en la citación de {nombre} el {cuando} (se cita a: {citadoA.ToLower()}).\nMotivo: {motivo}", "/Psychosocial");
            // "Informar también": solo si el profesional lo marcó (el otro la ve, pero responde el citado/a)
            if (informarOtro && citadoA == "Alumno" && est.AccesoPropio && apoderados.Count > 0)
                await _notif.NotificarAsync(apoderados, "Citación de su pupilo/a",
                    $"{nombre} fue citado/a por el equipo psicosocial el {cuando}. La confirma el alumno/a; usted puede verla en AUKA.", "/Portafolio?estudianteId=" + est.Id);
            if (informarOtro && citadoA == "Apoderado" && est.AccesoPropio)
                await _notif.NotificarAsync(new[] { est.UsuarioId }, "Citación a tu apoderado/a",
                    $"El equipo psicosocial citó a tu apoderado/a el {cuando}.", "/Portafolio?estudianteId=" + est.Id);
        }
        catch { /* los avisos extra no deben impedir la citación */ }

        return Json(new
        {
            ok = true,
            avisados = r.Total,
            porCorreo = r.Enviados,
            simulados = r.Simulados,
            fallidos = r.Fallidos,
            errorCorreo = r.UltimoError,
            soloPlataforma = r.SinCorreo
        });
    }

    // ---------------------------------------------------------------
    // CALENDARIO COMPARTIDO (psicóloga, trabajador social, orientadora)
    // El filtro multitenant del DbContext limita todo al colegio de la sesión.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/EventosCalendario?desde=2026-10-01&hasta=2026-10-31
    [HttpGet]
    public async Task<IActionResult> EventosCalendario(string desde, string hasta)
    {
        if (!DateOnly.TryParse(desde, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
            !DateOnly.TryParse(hasta, CultureInfo.InvariantCulture, DateTimeStyles.None, out var h))
        {
            return BadRequest("Rango de fechas inválido.");
        }

        var uid = UsuarioActualId();
        var marca = "," + uid + ",";
        var q = _context.EventosCalendario.AsNoTracking().Where(e => e.Fecha >= d && e.Fecha <= h);
        if (await CalendarioPropioAsync())
            q = q.Where(e => e.CreadoPorId == uid || (e.VisiblePara != null && ("," + e.VisiblePara + ",").Contains(marca)));   // agenda separada: la suya y a lo que lo sumaron
        else
            q = q.Where(e => e.VisiblePara == null || e.CreadoPorId == uid || ("," + e.VisiblePara + ",").Contains(marca));   // VisiblePara no es null en este lado del OR
        var lista = await q.OrderBy(e => e.Fecha).ThenBy(e => e.Hora).ToListAsync();

        return Json(lista.Select(e => new
        {
            id = e.Id,
            f = e.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            h = e.Hora.HasValue ? e.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            t = e.Tipo,
            x = e.Titulo,
            r = e.Responsable,
            ok = e.Realizada,
            mio = e.CreadoPorId == uid,              // solo quien la creó la edita (la orientadora no edita lo de otros)
            vis = e.VisiblePara ?? ""                 // "" = todo el equipo
        }));
    }

    // POST: crea (id = 0) o edita una nota
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarEvento(int id, string fecha, string? hora, string tipo,
        string titulo, string responsable, bool realizada, string? visiblePara = null)
    {
        if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return BadRequest("Fecha inválida.");
        if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 200)
            return BadRequest("Título inválido.");
        if (!TiposValidos.Contains(tipo))
            return BadRequest("Tipo inválido.");

        TimeOnly? horaEvento = TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hh) ? hh : null;
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);

        EventoCalendario evento;
        if (id == 0)
        {
            evento = new EventoCalendario { CreadoPorId = usuarioId };
            _context.EventosCalendario.Add(evento);
        }
        else
        {
            var existente = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
            if (existente == null) return NotFound();
            if ((EsOrientadora || await CalendarioPropioAsync()) && existente.CreadoPorId != usuarioId) return StatusCode(403, new { error = "Esto es de otro profesional." });
            evento = existente;
            evento.ModificadoPorId = usuarioId;
        }

        evento.Fecha = f;
        evento.Hora = horaEvento;
        evento.Tipo = tipo;
        evento.Titulo = titulo.Trim();
        evento.Responsable = string.IsNullOrWhiteSpace(responsable) ? "Equipo psicosocial" : responsable;
        evento.Realizada = realizada;
        if (evento.CreadoPorId == usuarioId) evento.VisiblePara = await VisibleParaValidoAsync(visiblePara, usuarioId);

        await _context.SaveChangesAsync();
        return Json(new { id = evento.Id });
    }

    // POST: marca o desmarca una nota como realizada
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarEvento(int id, bool realizada)
    {
        var evento = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
        if (evento == null) return NotFound();

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        if ((EsOrientadora || await CalendarioPropioAsync()) && evento.CreadoPorId != usuarioId) return StatusCode(403, new { error = "Esto es de otro profesional." });
        evento.Realizada = realizada;
        evento.ModificadoPorId = usuarioId;

        await _context.SaveChangesAsync();
        return Json(new { id = evento.Id });
    }

    // POST: elimina una nota (borrado lógico: el DbContext marca IsDeleted)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarEvento(int id)
    {
        var evento = await _context.EventosCalendario.FirstOrDefaultAsync(x => x.Id == id);
        if (evento == null) return NotFound();
        if ((EsOrientadora || await CalendarioPropioAsync()) && evento.CreadoPorId != UsuarioActualId()) return StatusCode(403, new { error = "Esto es de otro profesional." });

        _context.EventosCalendario.Remove(evento);
        await _context.SaveChangesAsync();
        return Json(new { id });
    }

    // ---------------------------------------------------------------
    // BENEFICIOS: sets pro-retención e inventario de ropa (compartidos por el equipo)
    // ---------------------------------------------------------------

    // GET: /Psychosocial/Beneficios
    [HttpGet]
    public async Task<IActionResult> Beneficios()
    {
        var sets = await _context.SetsBeneficio.AsNoTracking().ToListAsync();
        if (sets.Count == 0)
        {
            // Primera vez del colegio: se crean los dos sets que mencionó el equipo (editables)
            var anioNuevo = DateTime.Now.Year;
            _context.SetsBeneficio.AddRange(
                new SetBeneficio { Anio = anioNuevo, Nombre = "Set 1", Articulos = "Mochila\nPoleras\nParka\nZapatos\nÚtiles escolares", Orden = 1 },
                new SetBeneficio { Anio = anioNuevo, Nombre = "Set 2", Articulos = "Tablet", Orden = 2 });
            await _context.SaveChangesAsync();
            sets = await _context.SetsBeneficio.AsNoTracking().ToListAsync();
        }

        var anio = sets.Max(x => x.Anio);
        var inventario = await _context.ArticulosInventario.AsNoTracking().OrderBy(a => a.Nombre).ToListAsync();

        return Json(new
        {
            anio,
            sets = sets.Where(x => x.Anio == anio).OrderBy(x => x.Orden).Select(x => new
            {
                id = x.Id,
                n = x.Nombre,
                it = x.Articulos.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                t = x.CantidadAEntregar,
                e = x.Entregados
            }),
            inv = inventario.Select(a => new { id = a.Id, n = a.Nombre, ta = a.Tallas, s = a.Stock, m = a.Minimo })
        });
    }

    // POST: guarda los sets de un año (crea, edita y quita los que ya no están)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarSets(int anio, string setsJson)
    {
        List<SetDto>? lista;
        try { lista = JsonSerializer.Deserialize<List<SetDto>>(setsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return BadRequest("Datos inválidos."); }
        if (lista == null || anio < 2000 || anio > 2100) return BadRequest("Datos inválidos.");

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        var existentes = await _context.SetsBeneficio.Where(x => x.Anio == anio).ToListAsync();
        var conservados = new HashSet<SetBeneficio>();
        var orden = 1;

        foreach (var d in lista.Where(x => !string.IsNullOrWhiteSpace(x.n)))
        {
            var set = d.id > 0 ? existentes.FirstOrDefault(x => x.Id == d.id) : null;
            if (set == null)
            {
                set = new SetBeneficio { Anio = anio };
                _context.SetsBeneficio.Add(set);
            }
            else conservados.Add(set);

            set.Nombre = d.n.Trim();
            set.Articulos = string.Join("\n", (d.it ?? Array.Empty<string>()).Select(i => i.Trim()).Where(i => i.Length > 0));
            set.CantidadAEntregar = Math.Max(0, d.t);
            set.Entregados = Math.Max(0, d.e);
            set.Orden = orden++;
            set.ModificadoPorId = usuarioId;
        }

        foreach (var quitado in existentes.Where(x => !conservados.Contains(x)))
            _context.SetsBeneficio.Remove(quitado); // borrado lógico

        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: ingreso de stock (crea el artículo si no existe) y deja el movimiento registrado
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IngresarStock(string nombre, string? tallas, int cantidad)
    {
        if (string.IsNullOrWhiteSpace(nombre) || cantidad < 1 || cantidad > 100000) return BadRequest("Datos inválidos.");
        nombre = nombre.Trim();
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);

        var articulo = await _context.ArticulosInventario.FirstOrDefaultAsync(a => a.Nombre.ToLower() == nombre.ToLower());
        if (articulo == null)
        {
            articulo = new ArticuloInventario { Nombre = nombre, Tallas = tallas?.Trim() ?? "", Minimo = 3 };
            _context.ArticulosInventario.Add(articulo);
        }
        articulo.Stock += cantidad;
        articulo.ModificadoPorId = usuarioId;
        await _context.SaveChangesAsync();

        _context.MovimientosInventario.Add(new MovimientoInventario
        {
            ArticuloInventarioId = articulo.Id,
            Tipo = "Ingreso",
            Cantidad = cantidad,
            CreadoPorId = usuarioId
        });
        await _context.SaveChangesAsync();
        return Json(new { id = articulo.Id });
    }

    // POST: corrige el stock y el mínimo de un artículo (queda el movimiento "Ajuste")
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AjustarInventario(int id, int stock, int minimo)
    {
        var articulo = await _context.ArticulosInventario.FirstOrDefaultAsync(a => a.Id == id);
        if (articulo == null) return NotFound();

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId);
        stock = Math.Max(0, stock);
        var diferencia = stock - articulo.Stock;
        articulo.Stock = stock;
        articulo.Minimo = Math.Max(0, minimo);
        articulo.ModificadoPorId = usuarioId;

        if (diferencia != 0)
        {
            _context.MovimientosInventario.Add(new MovimientoInventario
            {
                ArticuloInventarioId = articulo.Id,
                Tipo = "Ajuste",
                Cantidad = diferencia,
                Detalle = "Corrección manual",
                CreadoPorId = usuarioId
            });
        }

        await _context.SaveChangesAsync();
        return Json(new { id = articulo.Id });
    }
    // ---------------------------------------------------------------
    // ALUMNOS/AS: listado, alta manual e importación desde CSV
    // Cada alumno/a crea su cuenta (rol Estudiante) con clave provisoria y queda en su curso.
    // ---------------------------------------------------------------

    // GET: /Psychosocial/Alumnos
    [HttpGet]
    public async Task<IActionResult> Alumnos()
    {
        var col = (await OrgAsync()).V.EsColegio;
        var lista = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Curso != null)
            .ToListAsync();

        return Json(lista
            .OrderBy(e => e.Curso!.Nombre).ThenBy(e => e.Curso!.Letra).ThenBy(e => e.Usuario!.Apellido)
            .Select(e => new
            {
                id = e.Id,
                n = $"{e.Usuario!.Nombre} {e.Usuario.Apellido}".Trim(),
                r = e.Usuario.Rut,
                c = col ? NombreCurso(e.Curso) : "",
                f = e.FechaNacimiento.HasValue ? e.FechaNacimiento.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                d = e.Direccion ?? "",
                t = e.Telefono ?? "",
                m = e.CorreoContacto ?? "",
                acc = e.AccesoPropio
            }));
    }

    // POST: importa un CSV (columnas: Nombre, RUT, Curso y, opcional, Fecha de nacimiento)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarAlumnos(IFormFile? archivo, bool crearCursos = true, bool actualizar = true)
    {
        if (archivo == null || archivo.Length == 0) return BadRequest(new { error = "Elige un archivo CSV." });
        if (archivo.Length > 2_000_000) return BadRequest(new { error = "El archivo es demasiado grande (máximo 2 MB)." });
        if (!archivo.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Por ahora solo se aceptan archivos CSV." });

        var lineas = new List<string>();
        using (var lector = new StreamReader(archivo.OpenReadStream(), Encoding.UTF8, true))
        {
            string? linea;
            while ((linea = await lector.ReadLineAsync()) != null)
                if (!string.IsNullOrWhiteSpace(linea)) lineas.Add(linea);
        }
        if (lineas.Count < 2) return BadRequest(new { error = "El archivo no tiene filas de alumnos/as." });
        if (lineas.Count > 2001) return BadRequest(new { error = "Máximo 2.000 alumnos/as por archivo." });

        var sep = lineas[0].Count(c => c == ';') >= lineas[0].Count(c => c == ',') ? ';' : ',';
        var cab = lineas[0].Split(sep).Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToList();
        int Col(params string[] claves) => cab.FindIndex(h => claves.Any(k => h.StartsWith(k)));
        int iNom = Col("nombre"), iRut = Col("rut"), iCur = Col("curso"), iNac = Col("fecha", "nacimiento");
        int iApo = Col("apoderado"), iCor = Col("correo"), iTel = Col("tel"), iDir = Col("direc", "domicilio");
        var esColegio = (await OrgAsync()).V.EsColegio;
        if (iNom < 0 || iRut < 0 || (esColegio && iCur < 0))
            return BadRequest(new { error = esColegio ? "Faltan columnas obligatorias: Nombre, RUT y Curso." : "Faltan columnas obligatorias: Nombre y RUT." });

        string Celda(string[] p, int i) => i >= 0 && i < p.Length ? p[i].Trim().Trim('"').Trim() : "";
        var filas = new List<FilaAlumno>();
        for (var i = 1; i < lineas.Count; i++)
        {
            var p = lineas[i].Split(sep);
            filas.Add(esColegio
                ? new FilaAlumno(i + 1, Celda(p, iNom), Celda(p, iRut), Celda(p, iCur), Celda(p, iNac), Celda(p, iApo), Celda(p, iCor), Celda(p, iTel), Celda(p, iDir))
                // Paciente: el correo y el teléfono son suyos (no de un apoderado/a)
                : new FilaAlumno(i + 1, Celda(p, iNom), Celda(p, iRut), "", Celda(p, iNac), Direccion: Celda(p, iDir), Telefono: Celda(p, iTel), Correo: Celda(p, iCor)));
        }

        return Json(await ProcesarAlumnosAsync(filas, crearCursos, actualizar));
    }

    // POST: alta manual de uno o varios alumnos/as
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarAlumnos(string alumnosJson)
    {
        List<AlumnoDto>? lista;
        try { lista = JsonSerializer.Deserialize<List<AlumnoDto>>(alumnosJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return BadRequest(new { error = "Datos inválidos." }); }
        if (lista == null || lista.Count == 0 || lista.Count > 200)
            return BadRequest(new { error = "Agrega entre 1 y 200 alumnos/as." });

        var filas = lista.Select((a, i) => new FilaAlumno(i + 1, a.n?.Trim() ?? "", a.r?.Trim() ?? "", a.c?.Trim() ?? "", a.f, a.ap?.Trim(), a.ce?.Trim(), a.te?.Trim(),
            a.d?.Trim(), a.t?.Trim(), a.m?.Trim())).ToList();
        return Json(await ProcesarAlumnosAsync(filas, true, true));
    }

    // ---------------------------------------------------------------
    // SEGUNDA CLAVE Y EXPEDIENTE CONFIDENCIAL DEL ALUMNO/A
    // Cada profesional crea su propia segunda clave la primera vez; al ingresarla se
    // desbloquean las fichas por 15 minutos. Cada apertura queda registrada.
    // ---------------------------------------------------------------

    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    private string LlaveDesbloqueo() => $"fichas-desbloqueadas:{UsuarioActualId()}";
    private bool FichasDesbloqueadas() => _cache.TryGetValue(LlaveDesbloqueo(), out _);
    private IActionResult Bloqueado() => StatusCode(403, new { bloqueada = true, error = "Vuelve a ingresar tu segunda clave." });

    private static bool CoincideClave(string email, string? hash, string clave)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try { return new PasswordHasher<string>().VerifyHashedPassword(email, hash, clave) != PasswordVerificationResult.Failed; }
        catch { return false; }
    }

    // GET: ¿tiene segunda clave? ¿está desbloqueada?
    [HttpGet]
    public async Task<IActionResult> EstadoClaveFicha()
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid);
        return Json(new { tieneClave = !string.IsNullOrEmpty(u?.ClaveFichaHash), desbloqueada = FichasDesbloqueadas() });
    }

    // POST: crea la segunda clave (solo si aún no tiene)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearClaveFicha(string clave, string confirmar)
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (!string.IsNullOrEmpty(u.ClaveFichaHash)) return BadRequest(new { error = "Ya tienes una segunda clave." });
        if (string.IsNullOrWhiteSpace(clave) || clave.Length < 6) return BadRequest(new { error = "La segunda clave debe tener al menos 6 caracteres." });
        if (clave != confirmar) return BadRequest(new { error = "Las claves no coinciden." });
        if (CoincideClave(u.Email, u.PasswordHash, clave)) return BadRequest(new { error = "Debe ser distinta de tu clave de ingreso." });

        u.ClaveFichaHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        await _context.SaveChangesAsync();
        _cache.Set(LlaveDesbloqueo(), true, TimeSpan.FromMinutes(15));
        return Json(new { ok = true });
    }

    // POST: "¿Olvidaste tu segunda clave?" -> se crea una nueva confirmando con la clave de ingreso.
    // (Más adelante: también con un código enviado al correo.)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecuperarClaveFicha(string claveIngreso, string clave, string confirmar)
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (u.ClaveFichaBloqueadaHasta > DateTime.UtcNow) return StatusCode(429, new { error = "Demasiados intentos. Espera unos minutos e inténtalo de nuevo." });

        var okIngreso = !string.IsNullOrWhiteSpace(claveIngreso) &&
            (CoincideClave(u.Email, u.PasswordHash, claveIngreso) || u.PasswordHash == claveIngreso);   // cuentas de prueba antiguas
        if (!okIngreso)
        {
            u.ClaveFichaIntentos++;
            if (u.ClaveFichaIntentos >= 5) { u.ClaveFichaBloqueadaHasta = DateTime.UtcNow.AddMinutes(5); u.ClaveFichaIntentos = 0; }
            await _context.SaveChangesAsync();
            return BadRequest(new { error = "Tu clave de ingreso no es correcta." });
        }
        if (string.IsNullOrWhiteSpace(clave) || clave.Length < 6) return BadRequest(new { error = "La nueva segunda clave debe tener al menos 6 caracteres." });
        if (clave != confirmar) return BadRequest(new { error = "Las claves nuevas no coinciden." });
        if (clave == claveIngreso) return BadRequest(new { error = "Debe ser distinta de tu clave de ingreso." });

        u.ClaveFichaHash = new PasswordHasher<string>().HashPassword(u.Email, clave);
        u.ClaveFichaIntentos = 0;
        u.ClaveFichaBloqueadaHasta = null;
        await _context.SaveChangesAsync();
        _cache.Set(LlaveDesbloqueo(), true, TimeSpan.FromMinutes(15));
        return Json(new { ok = true });
    }

    // POST: desbloquea las fichas por 15 minutos (5 intentos fallidos bloquean 5 minutos)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DesbloquearFichas(string clave)
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (string.IsNullOrEmpty(u.ClaveFichaHash)) return BadRequest(new { error = "Primero crea tu segunda clave." });
        if (u.ClaveFichaBloqueadaHasta > DateTime.UtcNow) return StatusCode(429, new { error = "Demasiados intentos. Espera unos minutos e inténtalo de nuevo." });

        if (!CoincideClave(u.Email, u.ClaveFichaHash, clave ?? ""))
        {
            u.ClaveFichaIntentos++;
            if (u.ClaveFichaIntentos >= 5) { u.ClaveFichaBloqueadaHasta = DateTime.UtcNow.AddMinutes(5); u.ClaveFichaIntentos = 0; }
            await _context.SaveChangesAsync();
            return BadRequest(new { error = "Segunda clave incorrecta." });
        }

        u.ClaveFichaIntentos = 0;
        u.ClaveFichaBloqueadaHasta = null;
        await _context.SaveChangesAsync();
        _cache.Set(LlaveDesbloqueo(), true, TimeSpan.FromMinutes(15));
        return Json(new { ok = true });
    }

    // GET: /Psychosocial/Expediente/5  (exige fichas desbloqueadas y deja registro del acceso)
    [HttpGet]
    public async Task<IActionResult> Expediente(int id)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();

        var est = await _context.Estudiantes.AsNoTracking()
            .Include(e => e.Usuario).Include(e => e.Curso)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (est?.Usuario == null || est.Curso == null) return NotFound();

        _context.AccesosExpediente.Add(new AccesoExpediente { EstudianteId = id, UsuarioId = UsuarioActualId() });
        await _context.SaveChangesAsync();

        var uidFicha = UsuarioActualId();
        var bitacoraPropia = await BitacoraPropiaAsync();
        var bitacora = await _context.BitacorasPsicosociales.AsNoTracking()
            .Where(b => b.EstudianteId == id && (!bitacoraPropia || b.ProfesionalId == uidFicha))   // separada: solo las notas propias
            .OrderByDescending(b => b.FechaAtencion).ToListAsync();
        var autoresIds = bitacora.Select(b => b.ProfesionalId).Distinct().ToList();
        var autores = await _context.Usuarios.AsNoTracking().Where(u => autoresIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.Nombre} {u.Apellido}".Trim());

        var ocultasFicha = await FichasOcultasAsync();
        var registros = await _context.RegistrosPsicosociales.AsNoTracking()
            .Where(r => r.EstudianteId == id && !ocultasFicha.Contains(r.Tipo)).OrderByDescending(r => r.FechaUtc).ToListAsync();
        var esColegio = (await OrgAsync()).V.EsColegio;
        var citaciones = await _context.Citaciones.AsNoTracking()
            .Where(c => c.EstudianteId == id).OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Hora).Take(30).ToListAsync();

        return Json(new
        {
            id = est.Id,
            n = $"{est.Usuario.Nombre} {est.Usuario.Apellido}".Trim(),
            r = est.Usuario.Rut,
            c = esColegio ? NombreCurso(est.Curso) : "",
            f = est.FechaNacimiento.HasValue ? est.FechaNacimiento.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
            d = est.Direccion ?? "",
            t = est.Telefono ?? "",
            m = est.CorreoContacto ?? "",
            acc = est.AccesoPropio,
            aps = esColegio ? await ContactosAsync(est.Id) : new List<object>(),
            email = est.Usuario.Email,
            bit = bitacora.Select(b => new
            {
                id = b.Id,
                t = b.Observaciones,
                f = b.FechaAtencion.ToString("o", CultureInfo.InvariantCulture),
                a = autores.GetValueOrDefault(b.ProfesionalId, "—"),
                m = b.FechaUltimaModificacion.HasValue
            }),
            reg = registros.Select(r => ProyectarRegistro(r, esColegio)),
            hoy = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cit = citaciones.Select(c => new
            {
                id = c.Id,
                f = c.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                h = c.Hora.HasValue ? c.Hora.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "",
                a = c.CitadoA,
                m = c.Motivo,
                e = (int)c.Estado
            })
        });
    }

    // POST: agrega una anotación a la bitácora confidencial
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarBitacora(int estudianteId, string observaciones)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        if (string.IsNullOrWhiteSpace(observaciones) || observaciones.Length > 4000) return BadRequest(new { error = "Escribe una anotación (máximo 4.000 caracteres)." });
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound();

        _context.BitacorasPsicosociales.Add(new BitacoraPsicosocial
        {
            EstudianteId = estudianteId,
            ProfesionalId = UsuarioActualId(),
            Observaciones = observaciones.Trim()
        });
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: corrige una anotación (la ve todo el equipo y queda marcada como editada)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarBitacora(int id, string observaciones)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        if (string.IsNullOrWhiteSpace(observaciones) || observaciones.Length > 4000) return BadRequest(new { error = "Escribe una anotación (máximo 4.000 caracteres)." });
        var b = await _context.BitacorasPsicosociales.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        if (await BitacoraPropiaAsync() && b.ProfesionalId != UsuarioActualId()) return StatusCode(403, new { error = "Esto es de otro profesional." });

        b.Observaciones = observaciones.Trim();
        b.ModificadoPorId = UsuarioActualId();
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: genera una nueva clave provisoria de ingreso para el alumno/a (la anterior deja de servir)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestablecerClaveAlumno(int estudianteId)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        var est = await _context.Estudiantes.Include(e => e.Usuario).FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null) return NotFound();

        var clave = GenerarClave();
        est.Usuario.PasswordHash = new PasswordHasher<string>().HashPassword(est.Usuario.Email, clave);
        est.Usuario.DebeCambiarPassword = true;
        await _context.SaveChangesAsync();
        return Json(new { email = est.Usuario.Email, clave });
    }

    // Apoderados/as y familiares vinculados (nombre, parentesco, correo y teléfono). Solo datos de contacto.
    private async Task<List<object>> ContactosAsync(int estudianteId)
    {
        var l = await _context.EstudiantesApoderados.AsNoTracking()
            .Where(x => x.EstudianteId == estudianteId && x.Apoderado != null && x.Apoderado.Usuario != null)
            .Select(x => new { x.ApoderadoId, x.Parentesco, x.EsSostenedorPrincipal, x.Apoderado!.TelefonoContacto, x.Apoderado.Usuario!.Nombre, x.Apoderado.Usuario.Apellido, x.Apoderado.Usuario.Email, x.Apoderado.Usuario.EmailPersonal })
            .ToListAsync();
        // Otros pupilos de cada apoderado/a (hermanos/as en el colegio)
        var ids = l.Select(x => x.ApoderadoId).ToList();
        var otros = (await _context.EstudiantesApoderados.AsNoTracking()
            .Where(x => ids.Contains(x.ApoderadoId) && x.EstudianteId != estudianteId && x.Estudiante != null && x.Estudiante.Usuario != null)
            .Select(x => new { x.ApoderadoId, n = x.Estudiante!.Usuario!.Nombre + " " + x.Estudiante.Usuario.Apellido })
            .ToListAsync()).GroupBy(x => x.ApoderadoId).ToDictionary(g => g.Key, g => g.Select(x => x.n.Trim()).ToArray());
        return l.OrderByDescending(x => x.EsSostenedorPrincipal).Select(x => (object)new
        {
            id = x.ApoderadoId,
            otros = otros.GetValueOrDefault(x.ApoderadoId, Array.Empty<string>()),
            n = $"{x.Nombre} {x.Apellido}".Trim(),
            p = string.IsNullOrWhiteSpace(x.Parentesco) ? "Apoderado/a" : x.Parentesco,
            tit = x.EsSostenedorPrincipal,
            mail = string.Join(", ", NotificacionService.Destinos(new Usuario { Email = x.Email, EmailPersonal = x.EmailPersonal })),   // sin correos internos
            tel = x.TelefonoContacto ?? ""
        }).ToList();
    }

    // GET: contacto del alumno/a (sin datos clínicos) para la orientadora y el equipo:
    // curso, dirección, teléfono y quién es su apoderado/a o familiar, con su correo y teléfono.
    [HttpGet]
    public async Task<IActionResult> Contacto(int id)
    {
        if (!(await OrgAsync()).V.EsColegio) return NotFound();
        var est = await _context.Estudiantes.AsNoTracking().Include(e => e.Usuario).Include(e => e.Curso).FirstOrDefaultAsync(e => e.Id == id);
        if (est?.Usuario == null) return NotFound();
        return Json(new
        {
            c = NombreCurso(est.Curso),
            d = est.Direccion ?? "",
            t = est.Telefono ?? "",
            m = est.CorreoContacto ?? "",
            acc = est.AccesoPropio,
            aps = await ContactosAsync(est.Id)
        });
    }

    // ---------------------------------------------------------------
    // APODERADOS/AS: crear, vincular a uno o más alumnos/as, quitar vínculo y dar acceso.
    // Un alumno/a puede tener varios (mamá, papá, abuela...) y un apoderado/a varios pupilos.
    // Lo hacen la orientadora y el equipo (por confirmar con el colegio si también UTP).
    // ---------------------------------------------------------------

    // Confirma una acción delicada con la clave de quien la hace (segunda clave si la tiene; si no, la de ingreso)
    private async Task<IActionResult?> ValidarClavePropiaAsync(string? clave)
    {
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (u.ClaveFichaBloqueadaHasta > DateTime.UtcNow) return StatusCode(429, new { error = "Demasiados intentos. Espera unos minutos e inténtalo de nuevo." });
        var hash = string.IsNullOrEmpty(u.ClaveFichaHash) ? u.PasswordHash : u.ClaveFichaHash;
        var ok = !string.IsNullOrWhiteSpace(clave) &&
            (CoincideClave(u.Email, hash, clave) || (string.IsNullOrEmpty(u.ClaveFichaHash) && u.PasswordHash == clave));
        if (!ok)
        {
            u.ClaveFichaIntentos++;
            if (u.ClaveFichaIntentos >= 5) { u.ClaveFichaBloqueadaHasta = DateTime.UtcNow.AddMinutes(5); u.ClaveFichaIntentos = 0; }
            await _context.SaveChangesAsync();
            return BadRequest(new { error = "Clave incorrecta." });
        }
        u.ClaveFichaIntentos = 0;
        u.ClaveFichaBloqueadaHasta = null;
        return null;
    }

    // Apoderado/a de un alumno/a de este colegio (el filtro multitenant de Estudiantes lo asegura)
    private Task<Apoderado?> ApoderadoDelColegioAsync(int apoderadoId) =>
        _context.Apoderados.Include(a => a.Usuario)
            .FirstOrDefaultAsync(a => a.Id == apoderadoId && _context.EstudiantesApoderados.Any(x => x.ApoderadoId == a.Id && _context.Estudiantes.Any(e => e.Id == x.EstudianteId)));

    // POST: agrega un apoderado/a al alumno/a. Si ya existe (mismo correo o RUT), solo lo vincula:
    // así un mismo apoderado/a queda con todos sus pupilos.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarApoderado(int estudianteId, string nombre, string? rut, string correo, string? telefono, string? parentesco, bool titular = false)
    {
        if (!(await OrgAsync()).V.EsColegio) return BadRequest(new { error = "Solo aplica a colegios." });
        var est = await _context.Estudiantes.Include(e => e.Usuario).FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null) return NotFound();

        correo = (correo ?? "").Trim().ToLowerInvariant();
        if (!EsCorreo(correo) || correo.EndsWith("@alumnos.auka.cl") || correo.EndsWith("@pacientes.auka.cl"))
            return BadRequest(new { error = "Escribe el correo real del apoderado/a (ahí le llegan los avisos)." });
        string rutFmt = "", rutNorm = "";
        if (!string.IsNullOrWhiteSpace(rut) && !TryNormalizarRut(rut, out rutNorm, out rutFmt)) return BadRequest(new { error = "El RUT no es válido." });
        parentesco = string.IsNullOrWhiteSpace(parentesco) ? "Apoderado/a" : parentesco.Trim();
        if (parentesco.Length > 40) parentesco = parentesco[..40];
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var tenantId);

        // ¿Ya existe? (por correo, o por RUT entre los apoderados/as de este colegio)
        var u = await _context.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Email.ToLower() == correo);
        if (u == null && rutNorm != "")
        {
            var candidatos = await _context.Usuarios.IgnoreQueryFilters().Where(x => x.Rol == RolUsuario.Apoderado && x.ColegioId == tenantId).ToListAsync();
            u = candidatos.FirstOrDefault(x => NormalizarRut(x.Rut) == rutNorm);
        }
        if (u != null && (u.Rol != RolUsuario.Apoderado || (tenantId > 0 && u.ColegioId != tenantId)))
            return BadRequest(new { error = "Ese correo ya pertenece a otra cuenta de AUKA (no de apoderado/a de este colegio). Usa otro correo." });

        var creado = false;
        Apoderado? apo = u == null ? null : await _context.Apoderados.FirstOrDefaultAsync(a => a.UsuarioId == u.Id);
        if (u == null)
        {
            var (nom, ape) = SepararNombre(string.IsNullOrWhiteSpace(nombre) ? "Apoderado/a" : nombre.Trim());
            u = new Usuario
            {
                Rut = rutFmt != "" ? rutFmt : "APO-" + NormalizarRut(est.Usuario.Rut),
                Nombre = nom,
                Apellido = ape,
                Email = correo,
                Rol = RolUsuario.Apoderado,
                ColegioId = tenantId > 0 ? tenantId : est.ColegioId,
                Activo = true,
                DebeCambiarPassword = true,
                PasswordHash = new PasswordHasher<string>().HashPassword(correo, GenerarClave())   // nadie la conoce: crea la suya con el enlace
            };
            creado = true;
        }
        if (apo == null) { apo = new Apoderado { Usuario = u, TelefonoContacto = telefono?.Trim() ?? "" }; _context.Apoderados.Add(apo); }
        else if (!string.IsNullOrWhiteSpace(telefono)) apo.TelefonoContacto = telefono.Trim();

        if (apo.Id > 0 && await _context.EstudiantesApoderados.AnyAsync(x => x.EstudianteId == est.Id && x.ApoderadoId == apo.Id))
            return BadRequest(new { error = "Ese apoderado/a ya está vinculado a este alumno/a." });

        var vinculos = await _context.EstudiantesApoderados.Where(x => x.EstudianteId == est.Id).ToListAsync();
        if (titular) vinculos.ForEach(v => v.EsSostenedorPrincipal = false);
        _context.EstudiantesApoderados.Add(new EstudianteApoderado
        {
            Estudiante = est,
            Apoderado = apo,
            Parentesco = parentesco,
            EsSostenedorPrincipal = titular || vinculos.Count == 0
        });
        await _context.SaveChangesAsync();

        // Cuenta nueva (o que aún no crea su contraseña): le llega un enlace a su correo (nunca se envía una clave)
        object? acceso = null;
        if (creado || u.DebeCambiarPassword)
        {
            var r = await _acceso.EnviarInvitacionAsync(u, $"{Request.Scheme}://{Request.Host}", User.FindFirst("ColegioNombre")?.Value ?? "Tu colegio");
            acceso = new { enviado = r.Enviado, simulado = r.Simulado, motivo = r.Motivo };
        }
        return Json(new { ok = true, creado, acceso, email = u.Email });
    }

    // POST: quita el vínculo apoderado/a - alumno/a (la cuenta del apoderado/a no se borra). Pide la clave.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarApoderado(int estudianteId, int apoderadoId, string? clave)
    {
        var err = await ValidarClavePropiaAsync(clave);
        if (err != null) return err;
        if (!await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId)) return NotFound();
        var v = await _context.EstudiantesApoderados.FirstOrDefaultAsync(x => x.EstudianteId == estudianteId && x.ApoderadoId == apoderadoId);
        if (v == null) return NotFound();
        _context.EstudiantesApoderados.Remove(v);
        if (v.EsSostenedorPrincipal)
        {
            var otro = await _context.EstudiantesApoderados.FirstOrDefaultAsync(x => x.EstudianteId == estudianteId && x.ApoderadoId != apoderadoId);
            if (otro != null) otro.EsSostenedorPrincipal = true;
        }
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: acceso del apoderado/a que olvidó su clave.
    // modo "correo": le llega un enlace para crear una nueva (recomendado).
    // modo "clave": clave provisoria que se muestra una sola vez (pide la clave de quien lo hace).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AccesoApoderado(int apoderadoId, string modo, string? clave)
    {
        var apo = await ApoderadoDelColegioAsync(apoderadoId);
        if (apo?.Usuario == null) return NotFound();
        if (modo == "clave")
        {
            var err = await ValidarClavePropiaAsync(clave);
            if (err != null) return err;
            var nueva = GenerarClave();
            apo.Usuario.PasswordHash = new PasswordHasher<string>().HashPassword(apo.Usuario.Email, nueva);
            apo.Usuario.DebeCambiarPassword = true;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, email = apo.Usuario.Email, clave = nueva });
        }
        var r = await _acceso.EnviarInvitacionAsync(apo.Usuario, $"{Request.Scheme}://{Request.Host}", User.FindFirst("ColegioNombre")?.Value ?? "Tu colegio");
        if (!r.Enviado && !r.Simulado) return BadRequest(new { error = r.Motivo ?? "No se pudo enviar el correo." });
        return Json(new { ok = true, simulado = r.Simulado, email = apo.Usuario.Email });
    }

    // GET: cursos del año para corregir el curso de un alumno/a; avisa qué clave se pide para confirmarlo
    [HttpGet]
    public async Task<IActionResult> ListaCursos()
    {
        var anio = DateTime.Now.Year;
        var cursos = await _context.Cursos.AsNoTracking().Where(c => c.AnioAcademico == anio && c.Nombre != "Pacientes").ToListAsync();
        var uid = UsuarioActualId();
        var segunda = await _context.Usuarios.AsNoTracking().Where(u => u.Id == uid).Select(u => u.ClaveFichaHash != null && u.ClaveFichaHash != "").FirstOrDefaultAsync();
        return Json(new
        {
            segunda,
            cursos = cursos.OrderBy(c => OrdenCurso(c.Nombre)).ThenBy(c => c.Nombre).ThenBy(c => c.Letra)
                .Select(c => new { id = c.Id, n = NombreCurso(c) })
        });
    }

    // Orden natural de los cursos: prekínder, kínder, 1° a 8° básico, I a IV medio
    private static int OrdenCurso(string n)
    {
        var x = (n ?? "").ToLowerInvariant();
        if (x.Contains("pre")) return 0;
        if (x.Contains("nder") || x.StartsWith("nt")) return 1;
        var m = System.Text.RegularExpressions.Regex.Match(x, @"^\s*(\d+)");
        if (x.Contains("medio"))
        {
            var r = System.Text.RegularExpressions.Regex.Match(x, @"^\s*(iv|iii|ii|i|\d)");
            var v = r.Groups[1].Value switch { "i" or "1" => 1, "ii" or "2" => 2, "iii" or "3" => 3, "iv" or "4" => 4, _ => 5 };
            return 20 + v;
        }
        return m.Success ? 1 + int.Parse(m.Groups[1].Value) : 50;
    }

    // POST: corrige el curso de un alumno/a. Siempre pide confirmar con la clave:
    // la segunda clave si la persona la tiene; si no (por ejemplo, la orientadora), su clave de ingreso.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarCurso(int estudianteId, int cursoId, string? clave)
    {
        if (!(await OrgAsync()).V.EsColegio) return BadRequest(new { error = "Solo aplica a colegios." });
        var uid = UsuarioActualId();
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == uid);
        if (u == null) return NotFound();
        if (u.ClaveFichaBloqueadaHasta > DateTime.UtcNow) return StatusCode(429, new { error = "Demasiados intentos. Espera unos minutos e inténtalo de nuevo." });

        var hash = string.IsNullOrEmpty(u.ClaveFichaHash) ? u.PasswordHash : u.ClaveFichaHash;
        var okClave = !string.IsNullOrWhiteSpace(clave) &&
            (CoincideClave(u.Email, hash, clave) || (string.IsNullOrEmpty(u.ClaveFichaHash) && u.PasswordHash == clave));   // cuentas de prueba antiguas
        if (!okClave)
        {
            u.ClaveFichaIntentos++;
            if (u.ClaveFichaIntentos >= 5) { u.ClaveFichaBloqueadaHasta = DateTime.UtcNow.AddMinutes(5); u.ClaveFichaIntentos = 0; }
            await _context.SaveChangesAsync();
            return BadRequest(new { error = "Clave incorrecta." });
        }
        u.ClaveFichaIntentos = 0;
        u.ClaveFichaBloqueadaHasta = null;

        // El filtro multitenant asegura que el alumno/a y el curso sean de este colegio
        var est = await _context.Estudiantes.Include(e => e.Curso).FirstOrDefaultAsync(e => e.Id == estudianteId);
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == cursoId && c.Nombre != "Pacientes");
        if (est == null || curso == null) return BadRequest(new { error = "Elige un curso de la lista." });
        if (est.CursoId == curso.Id) { await _context.SaveChangesAsync(); return Json(new { ok = true, c = NombreCurso(curso), acc = est.AccesoPropio }); }

        var antes = NombreCurso(est.Curso);
        est.CursoId = curso.Id;
        est.Curso = curso;
        // Kínder a 6° básico: sin cuenta propia; desde 7° básico, con cuenta propia
        est.AccesoPropio = !Citaciones.EsCursoMenor(curso.Nombre);

        // Queda registrado en la bitácora quién lo cambió y cuándo
        _context.BitacorasPsicosociales.Add(new BitacoraPsicosocial
        {
            EstudianteId = est.Id,
            ProfesionalId = uid,
            Observaciones = $"Cambio de curso: {(string.IsNullOrEmpty(antes) ? "sin curso" : antes)} → {NombreCurso(curso)}."
        });
        await _context.SaveChangesAsync();
        return Json(new { ok = true, c = NombreCurso(curso), acc = est.AccesoPropio });
    }

    // POST: el alumno/a usa o no la plataforma con su propia cuenta (si no, todo lo maneja su apoderado/a)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarAccesoPropio(int estudianteId, bool acceso)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        var est = await _context.Estudiantes.FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est == null) return NotFound();
        est.AccesoPropio = acceso;
        await _context.SaveChangesAsync();
        return Json(new { ok = true, acc = est.AccesoPropio });
    }

    // POST: cambia el correo con que la persona entra a AUKA (y donde le llegan las citaciones)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarCorreoAcceso(int estudianteId, string correo)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        var nuevo = (correo ?? "").Trim().ToLowerInvariant();
        if (!EsCorreo(nuevo)) return BadRequest(new { error = "Escribe un correo válido." });
        if (nuevo.EndsWith("@alumnos.auka.cl") || nuevo.EndsWith("@pacientes.auka.cl"))
            return BadRequest(new { error = "Usa el correo personal o de trabajo de la persona." });

        var est = await _context.Estudiantes.Include(e => e.Usuario).FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null) return NotFound();
        if (est.Usuario.Email.ToLower() == nuevo) return Json(new { ok = true, email = nuevo });

        // El correo de ingreso es único en toda la plataforma
        if (await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Id != est.UsuarioId && u.Email.ToLower() == nuevo))
            return BadRequest(new { error = "Ese correo ya lo usa otra cuenta en AUKA." });

        est.Usuario.Email = nuevo;      // la clave no cambia: PasswordHasher no depende del correo
        est.CorreoContacto = nuevo;
        await _context.SaveChangesAsync();
        return Json(new { ok = true, email = nuevo });
    }

    // POST: envía a la persona un correo con un enlace para crear su propia contraseña (no se envía ninguna clave)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnviarAccesoCorreo(int estudianteId)
    {
        if (!FichasDesbloqueadas()) return Bloqueado();
        var est = await _context.Estudiantes.Include(e => e.Usuario).FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null) return NotFound();

        var r = await _acceso.EnviarInvitacionAsync(est.Usuario, $"{Request.Scheme}://{Request.Host}", User.FindFirst("ColegioNombre")?.Value ?? "Tu organización");
        if (!r.Enviado && !r.Simulado) return BadRequest(new { error = r.Motivo ?? "No se pudo enviar el correo." });
        return Json(new { ok = true, simulado = r.Simulado, email = est.Usuario.Email });
    }

    private async Task<object> ProcesarAlumnosAsync(List<FilaAlumno> filas, bool crearCursos, bool actualizar)
    {
        var errores = new List<string>();
        var avisos = new List<string>();          // no impiden crear la cuenta
        var invitar = new List<Usuario>();         // pacientes con correo propio: reciben el enlace para crear su contraseña
        var credenciales = new List<object>();
        int creados = 0, actualizados = 0, omitidos = 0, cursosCreados = 0;
        var anio = DateTime.Now.Year;
        var esColegio = (await OrgAsync()).V.EsColegio;

        // El filtro multitenant limita todo esto al colegio de la sesión
        var cursos = await _context.Cursos.Where(c => c.AnioAcademico == anio).ToListAsync();
        var estudiantes = await _context.Estudiantes.Include(e => e.Usuario).ToListAsync();
        var porRut = estudiantes.Where(e => e.Usuario != null)
            .GroupBy(e => NormalizarRut(e.Usuario!.Rut)).ToDictionary(g => g.Key, g => g.First());
        var correos = (await _context.Usuarios.IgnoreQueryFilters()
            .Where(u => u.Email.EndsWith("@alumnos.auka.cl") || u.Email.EndsWith("@pacientes.auka.cl")).Select(u => u.Email).ToListAsync()).ToHashSet();

        var vistos = new HashSet<string>();
        var hasher = new PasswordHasher<string>();

        // ---- Apoderados/as: cuentas con su correo real (ahí llegan los avisos) ----
        int apoCreados = 0;
        _ = int.TryParse(User.FindFirst("ColegioId")?.Value, out var tenantId);
        var correosApo = filas.Select(x => (x.ApoCorreo ?? "").Trim().ToLowerInvariant()).Where(EsCorreo).Distinct().ToList();
        var usuariosApo = (await _context.Usuarios.IgnoreQueryFilters()
            .Where(u => correosApo.Contains(u.Email.ToLower())).ToListAsync())
            .GroupBy(u => u.Email.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var idsUsuApo = usuariosApo.Values.Select(u => u.Id).ToList();
        var apoPorUsuario = await _context.Apoderados.Where(a => idsUsuApo.Contains(a.UsuarioId)).ToDictionaryAsync(a => a.UsuarioId);
        var vinculos = (await _context.EstudiantesApoderados
            .Where(x => _context.Estudiantes.Any(e => e.Id == x.EstudianteId))
            .Select(x => new { x.EstudianteId, x.ApoderadoId }).ToListAsync())
            .Select(x => (x.EstudianteId, x.ApoderadoId)).ToHashSet();
        var nuevosApo = new Dictionary<string, Apoderado>();

        void Vincular(Estudiante est, FilaAlumno f, string rutNorm)
        {
            var correo = (f.ApoCorreo ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(correo) && string.IsNullOrWhiteSpace(f.ApoNombre)) return;
            if (!EsCorreo(correo)) { errores.Add($"Fila {f.Linea}: falta un correo válido del apoderado/a (el alumno/a sí quedó cargado/a)."); return; }

            Apoderado? apo = null;
            if (nuevosApo.TryGetValue(correo, out var ya)) apo = ya;
            else if (usuariosApo.TryGetValue(correo, out var u))
            {
                if (u.Rol != RolUsuario.Apoderado || (tenantId > 0 && u.ColegioId != tenantId))
                { errores.Add($"Fila {f.Linea}: el correo {correo} ya está registrado en otra cuenta."); return; }
                if (!apoPorUsuario.TryGetValue(u.Id, out apo))
                {
                    apo = new Apoderado { Usuario = u, TelefonoContacto = f.ApoTel ?? "" };
                    _context.Apoderados.Add(apo);
                    nuevosApo[correo] = apo;
                }
            }
            else
            {
                var clave = GenerarClave();
                var (nom, ape) = SepararNombre(string.IsNullOrWhiteSpace(f.ApoNombre) ? "Apoderado/a" : f.ApoNombre!);
                var usuario = new Usuario
                {
                    Rut = "APO-" + rutNorm,
                    Nombre = nom,
                    Apellido = ape,
                    Email = correo,
                    Rol = RolUsuario.Apoderado,
                    Activo = true,
                    DebeCambiarPassword = true,
                    PasswordHash = hasher.HashPassword(correo, clave)
                };
                apo = new Apoderado { Usuario = usuario, TelefonoContacto = f.ApoTel ?? "" };
                _context.Apoderados.Add(apo);
                nuevosApo[correo] = apo;
                credenciales.Add(new { rut = "", n = $"{f.ApoNombre} (apoderado/a de {f.Nombre})", e = correo, c = clave });
                apoCreados++;
            }

            if (est.Id > 0 && apo.Id > 0 && vinculos.Contains((est.Id, apo.Id))) return;
            _context.EstudiantesApoderados.Add(new EstudianteApoderado { Estudiante = est, Apoderado = apo, Parentesco = "Apoderado/a", EsSostenedorPrincipal = true });
            if (est.Id > 0 && apo.Id > 0) vinculos.Add((est.Id, apo.Id));
        }

        foreach (var f in filas)
        {
            if (string.IsNullOrWhiteSpace(f.Nombre)) { errores.Add($"Fila {f.Linea}: falta el nombre."); continue; }
            if (!TryNormalizarRut(f.Rut, out var rutNorm, out var rutFmt)) { errores.Add($"Fila {f.Linea}: RUT inválido ({f.Rut})."); continue; }
            if (!vistos.Add(rutNorm)) { errores.Add($"Fila {f.Linea}: RUT repetido en el archivo ({rutFmt})."); continue; }
            if (esColegio && string.IsNullOrWhiteSpace(f.Curso)) { errores.Add($"Fila {f.Linea}: falta el curso."); continue; }
            if (!string.IsNullOrWhiteSpace(f.Correo) && !EsCorreo(f.Correo.Trim().ToLowerInvariant())) { errores.Add($"Fila {f.Linea}: correo inválido ({f.Correo})."); continue; }
            var nac = ParseFecha(f.Nacimiento);
            if (!string.IsNullOrWhiteSpace(f.Nacimiento) && nac == null) { errores.Add($"Fila {f.Linea}: fecha de nacimiento inválida ({f.Nacimiento})."); continue; }

            // Fuera de un colegio no hay cursos: todos los pacientes quedan en un grupo interno "Pacientes" que no se muestra
            var (nombreCurso, letra) = esColegio ? SepararCurso(f.Curso) : ("Pacientes", "");
            var curso = cursos.FirstOrDefault(c =>
                string.Equals(c.Nombre.Trim(), nombreCurso, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((c.Letra ?? "").Trim(), letra, StringComparison.OrdinalIgnoreCase));
            if (curso == null)
            {
                if (!crearCursos && esColegio) { errores.Add($"Fila {f.Linea}: el curso «{f.Curso}» no existe."); continue; }
                curso = new Curso { Nombre = nombreCurso, Letra = letra, AnioAcademico = anio };
                _context.Cursos.Add(curso);
                cursos.Add(curso);
                cursosCreados++;
            }

            if (porRut.TryGetValue(rutNorm, out var existente))
            {
                if (!actualizar) { omitidos++; continue; }
                existente.Curso = curso;
                // Al pasar a 7° básico o más, el alumno/a pasa a tener su propia cuenta
                if (esColegio && !existente.AccesoPropio && !Citaciones.EsCursoMenor(nombreCurso)) existente.AccesoPropio = true;
                if (nac != null) existente.FechaNacimiento = nac;
                if (!string.IsNullOrWhiteSpace(f.Direccion)) existente.Direccion = f.Direccion.Trim();
                if (!string.IsNullOrWhiteSpace(f.Telefono)) existente.Telefono = f.Telefono.Trim();
                if (!string.IsNullOrWhiteSpace(f.Correo)) existente.CorreoContacto = f.Correo.Trim().ToLowerInvariant();
                if (esColegio) Vincular(existente, f, rutNorm);
                actualizados++;
                continue;
            }

            var email = $"{rutNorm.ToLowerInvariant()}@{(esColegio ? "alumnos" : "pacientes")}.auka.cl";
            var conCorreoPropio = false;
            // Paciente con correo propio: ese es su usuario (ahí le llegan las citaciones y la recuperación de clave)
            if (!esColegio && !string.IsNullOrWhiteSpace(f.Correo))
            {
                var real = f.Correo.Trim().ToLowerInvariant();
                if (!correos.Contains(real) && !await _context.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == real))
                {
                    email = real;
                    conCorreoPropio = true;
                }
                else avisos.Add($"Fila {f.Linea}: el correo {real} ya lo usa otra cuenta; {f.Nombre} quedó para entrar con su RUT {rutFmt}. Puedes cambiarle el correo desde su ficha.");
            }
            else if (!esColegio)
                avisos.Add($"Fila {f.Linea}: {f.Nombre} no tiene correo; entrará con su RUT {rutFmt}. Agrégale su correo desde la ficha para que reciba las citaciones.");
            if (correos.Contains(email)) { errores.Add($"Fila {f.Linea}: el RUT {rutFmt} ya está registrado en otro colegio."); continue; }

            var (nombres, apellidos) = SepararNombre(f.Nombre);
            var clave = GenerarClave();
            var usuario = new Usuario
            {
                Rut = rutFmt,
                Nombre = nombres,
                Apellido = apellidos,
                Email = email,
                Rol = RolUsuario.Estudiante,
                Activo = true,
                DebeCambiarPassword = true,
                PasswordHash = hasher.HashPassword(email, clave)
            };
            var nuevoEst = new Estudiante
            {
                Usuario = usuario,
                Curso = curso,
                FechaNacimiento = nac,
                Direccion = string.IsNullOrWhiteSpace(f.Direccion) ? null : f.Direccion.Trim(),
                Telefono = string.IsNullOrWhiteSpace(f.Telefono) ? null : f.Telefono.Trim(),
                CorreoContacto = string.IsNullOrWhiteSpace(f.Correo) ? null : f.Correo.Trim().ToLowerInvariant(),
                TipoMatricula = TipoMatricula.Nuevo,
                FechaMatricula = DateTime.UtcNow,
                // Kínder a 6° básico: sin cuenta propia (todo lo maneja su apoderado/a)
                AccesoPropio = !esColegio || !Citaciones.EsCursoMenor(nombreCurso)
            };
            _context.Estudiantes.Add(nuevoEst);
            if (esColegio) Vincular(nuevoEst, f, rutNorm);
            if (conCorreoPropio) invitar.Add(usuario);
            correos.Add(email);
            // Sin cuenta propia no se entrega clave (no puede entrar; su apoderado/a ve todo)
            if (nuevoEst.AccesoPropio) credenciales.Add(new { rut = rutFmt, n = f.Nombre, e = email, c = clave });
            creados++;
        }

        await _context.SaveChangesAsync();

        // Correo de bienvenida con enlace para crear la contraseña (máximo 50 por carga para no demorar la respuesta)
        var invitados = 0;
        var nombreOrg = User.FindFirst("ColegioNombre")?.Value ?? "Tu organización";
        foreach (var inv in invitar.Take(50))
        {
            try { if ((await _acceso.EnviarInvitacionAsync(inv, $"{Request.Scheme}://{Request.Host}", nombreOrg)).Enviado) invitados++; }
            catch { /* si el correo falla, igual quedan la clave provisoria y "¿Olvidaste tu contraseña?" */ }
        }

        return new
        {
            avisos = avisos.Take(30),
            invitados,
            creados,
            actualizados,
            omitidos,
            cursosCreados,
            apoderados = apoCreados,
            errores = errores.Take(30),
            totalErrores = errores.Count,
            credenciales
        };
    }

    private static bool EsCorreo(string? c) =>
        !string.IsNullOrWhiteSpace(c) && System.Text.RegularExpressions.Regex.IsMatch(c.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    private static string NormalizarRut(string? rut) =>
        new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static bool TryNormalizarRut(string? entrada, out string norm, out string formateado)
    {
        norm = formateado = "";
        var limpio = NormalizarRut(entrada);
        if (limpio.Length < 7 || limpio.Length > 9) return false;
        var cuerpo = limpio[..^1];
        var dv = limpio[^1];
        if (!cuerpo.All(char.IsDigit)) return false;

        var suma = 0; var factor = 2;
        foreach (var d in cuerpo.Reverse()) { suma += (d - '0') * factor; factor = factor == 7 ? 2 : factor + 1; }
        var resto = 11 - suma % 11;
        var esperado = resto == 11 ? '0' : resto == 10 ? 'K' : (char)('0' + resto);
        if (dv != esperado) return false;

        var partes = new List<string>();
        for (var i = cuerpo.Length; i > 0; i -= 3) partes.Insert(0, cuerpo.Substring(Math.Max(0, i - 3), Math.Min(3, i)));
        norm = limpio;
        formateado = string.Join(".", partes) + "-" + dv;
        return true;
    }

    private static (string nombre, string letra) SepararCurso(string texto)
    {
        var partes = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length >= 2 && partes[^1].Length == 1 && char.IsLetter(partes[^1][0]))
            return (string.Join(" ", partes[..^1]), partes[^1].ToUpperInvariant());
        return (string.Join(" ", partes), "");
    }

    private static (string nombres, string apellidos) SepararNombre(string completo)
    {
        var p = completo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 3) return (string.Join(" ", p[..^2]), string.Join(" ", p[^2..]));
        if (p.Length == 2) return (p[0], p[1]);
        return (completo.Trim(), "");
    }

    private static string GenerarClave()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)]).ToArray());
    }

    private static DateOnly? ParseFecha(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        return DateOnly.TryParseExact(texto.Trim(), new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // ---------------------------------------------------------------
    // CLAVES DE INGRESO DE ALUMNOS/AS (las claves viejas no se pueden leer: están con hash)
    // ---------------------------------------------------------------

    // POST: nuevas claves provisorias solo para quienes aún no han cambiado la suya
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerarClavesAlumnos(string? curso)
    {
        if (!EsAdministrador && !FichasDesbloqueadas()) return Bloqueado();
        var lista = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso)
            .Where(e => e.Usuario != null && e.Usuario.Activo && e.Usuario.DebeCambiarPassword && e.Curso != null && e.AccesoPropio)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(curso))
            lista = lista.Where(e => (string.IsNullOrEmpty(e.Curso!.Letra) ? e.Curso.Nombre : $"{e.Curso.Nombre} {e.Curso.Letra}")
                .StartsWith(curso, StringComparison.OrdinalIgnoreCase)).ToList();

        var hasher = new PasswordHasher<string>();
        var credenciales = new List<object>();
        foreach (var e in lista)
        {
            var clave = GenerarClave();
            e.Usuario!.PasswordHash = hasher.HashPassword(e.Usuario.Email, clave);
            credenciales.Add(new { rut = e.Usuario.Rut, n = $"{e.Usuario.Nombre} {e.Usuario.Apellido}".Trim(), e = e.Usuario.Email, c = clave });
        }

        // Apoderados/as que aún no cambian su clave
        var vincs = await _context.EstudiantesApoderados
            .Include(x => x.Apoderado).ThenInclude(p => p!.Usuario)
            .Include(x => x.Estudiante).ThenInclude(e => e!.Curso)
            .Where(x => x.Apoderado != null && x.Apoderado.Usuario != null && x.Apoderado.Usuario.Activo && x.Apoderado.Usuario.DebeCambiarPassword)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(curso))
            vincs = vincs.Where(x => x.Estudiante?.Curso != null &&
                (string.IsNullOrEmpty(x.Estudiante.Curso.Letra) ? x.Estudiante.Curso.Nombre : $"{x.Estudiante.Curso.Nombre} {x.Estudiante.Curso.Letra}")
                    .StartsWith(curso, StringComparison.OrdinalIgnoreCase)).ToList();
        var apoderados = vincs.Select(x => x.Apoderado!).GroupBy(p => p.Id).Select(g => g.First()).ToList();
        foreach (var a in apoderados)
        {
            var clave = GenerarClave();
            a.Usuario!.PasswordHash = hasher.HashPassword(a.Usuario.Email, clave);
            credenciales.Add(new { rut = "", n = $"{a.Usuario.Nombre} {a.Usuario.Apellido}".Trim() + " (apoderado/a)", e = a.Usuario.Email, c = clave });
        }

        await _context.SaveChangesAsync();
        return Json(new { total = credenciales.Count, credenciales });
    }

    // ---------------------------------------------------------------
    // REGISTROS DE LA PLANILLA PSICOSOCIAL ("+ Nuevo registro")
    // Una sola tabla para las 10 fichas; los campos van en JSON. Así se agregan fichas
    // nuevas (o se activan por organización) sin crear una tabla por planilla.
    // ---------------------------------------------------------------

    private static readonly Dictionary<string, string> TiposRegistro = new()
    {
        ["seg"] = "Seguimiento mensual",
        ["jud"] = "Caso judicializado",
        ["inf"] = "Informe evacuado",
        ["vd"] = "Visita domiciliaria",
        ["conv"] = "Derivación a convivencia escolar",
        ["tut"] = "Derivación a profesor tutor",
        ["plan"] = "Plan de acción",
        ["pro"] = "Beneficio pro-retención",
        ["ropa"] = "Movimiento de inventario",
        ["egr"] = "Egreso psicosocial",
        ["rein"] = "Reintegro escolar",
        // Protocolos de actuación del Reglamento Interno
        ["mal"] = "Protocolo de maltrato escolar",
        ["vul"] = "Vulneración de derechos",
        ["dec"] = "Desregulación emocional (DEC)",
        ["vit"] = "Protocolo de riesgo vital",
        // Trabajador/a social
        ["fs"] = "Ficha social familiar",
        ["beca"] = "Beca o beneficio estatal",
        ["red"] = "Coordinación con la red",
        ["entf"] = "Entrevista familiar",
        // Orientación
        ["ori"] = "Orientación vocacional",
        ["ento"] = "Entrevista de orientación",
        ["tal"] = "Taller grupal",
        // Lo que el equipo quiere que vea el profesor/a jefe y/o la familia (no es confidencial)
        ["comp"] = "Compromiso o nota compartida"
    };

    // Las mismas fichas con el nombre que usa una empresa, fundación o particular
    private static readonly Dictionary<string, string> TiposRegistroOrg = new()
    {
        ["seg"] = "Seguimiento mensual",
        ["jud"] = "Caso Ley Karin",
        ["inf"] = "Informe emitido",
        ["vd"] = "Visita en terreno",
        ["conv"] = "Derivación externa",
        ["tut"] = "Acuerdo con jefatura",
        ["plan"] = "Plan de intervención",
        ["pro"] = "Beneficio",
        ["ropa"] = "Movimiento de inventario",
        ["egr"] = "Alta / cierre de caso",
        ["rein"] = "Reintegro laboral",
        ["mal"] = "Protocolo de maltrato",
        ["vul"] = "Vulneración de derechos",
        ["dec"] = "Desregulación emocional",
        ["vit"] = "Protocolo de riesgo vital",
        ["fs"] = "Ficha social familiar",
        ["beca"] = "Beneficio social",
        ["red"] = "Coordinación con la red",
        ["entf"] = "Entrevista familiar",
        ["ori"] = "Orientación laboral",
        ["ento"] = "Entrevista de orientación",
        ["tal"] = "Taller grupal",
        ["comp"] = "Compromiso o nota compartida"
    };

    private static string NombreCurso(Curso? c) =>
        c == null ? "" : string.IsNullOrEmpty(c.Letra) ? c.Nombre : $"{c.Nombre} {c.Letra}";

    // Próximo correlativo de informes del año (cuenta también los eliminados para no repetir números)
    private async Task<string> SiguienteCorrelativoAsync()
    {
        var anio = DateTime.UtcNow.Year;
        var desde = new DateTime(anio, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var colegioId = _context.CurrentTenantId;
        var n = await _context.RegistrosPsicosociales.IgnoreQueryFilters()
            .CountAsync(r => r.ColegioId == colegioId && r.Tipo == "inf" && r.FechaUtc >= desde);
        var prefijo = (await OrgAsync()).V.EsColegio ? "FEMAR" : "INF";
        return $"{prefijo}-{n + 1}-{anio}";
    }

    // GET: /Psychosocial/SiguienteCorrelativo
    [HttpGet]
    public async Task<IActionResult> SiguienteCorrelativo() => Json(new { n = await SiguienteCorrelativoAsync() });

    // GET: /Psychosocial/Registros  -> todos los registros del colegio (llenan las pestañas de la planilla)
    [HttpGet]
    public async Task<IActionResult> Registros(string? tipo)
    {
        var q = _context.RegistrosPsicosociales.AsNoTracking()
            .Include(r => r.Estudiante).ThenInclude(e => e!.Usuario)
            .Include(r => r.Estudiante).ThenInclude(e => e!.Curso)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(r => r.Tipo == tipo);
        if (EsOrientadora) q = q.Where(r => TiposOrientacion.Contains(r.Tipo));
        var ocultas = await FichasOcultasAsync();
        if (ocultas.Length > 0) q = q.Where(r => !ocultas.Contains(r.Tipo));

        var lista = await q.OrderByDescending(r => r.FechaUtc).Take(3000).ToListAsync();
        var esColegio = (await OrgAsync()).V.EsColegio;
        return Json(lista.Select(r => ProyectarRegistro(r, esColegio)));
    }

    private static object ProyectarRegistro(RegistroPsicosocial r, bool esColegio)
    {
        Dictionary<string, string>? datos;
        try { datos = JsonSerializer.Deserialize<Dictionary<string, string>>(r.DatosJson); }
        catch { datos = null; }
        return new
        {
            id = r.Id,
            k = r.Tipo,
            nk = (esColegio ? TiposRegistro : TiposRegistroOrg).GetValueOrDefault(r.Tipo, r.Tipo),
            eid = r.EstudianteId,
            n = r.Estudiante?.Usuario != null ? $"{r.Estudiante.Usuario.Nombre} {r.Estudiante.Usuario.Apellido}".Trim() : "",
            c = esColegio ? NombreCurso(r.Estudiante?.Curso) : "",
            d = datos ?? new Dictionary<string, string>(),
            f = r.FechaUtc.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    // POST: guarda un registro nuevo de la planilla
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarRegistro(string tipo, int? estudianteId, string datosJson)
    {
        if (string.IsNullOrWhiteSpace(tipo) || !TiposRegistro.ContainsKey(tipo))
            return BadRequest(new { error = "Tipo de ficha no válido." });
        if (tipo == "tal" && estudianteId != null) estudianteId = null;
        if (!(await OrgAsync()).Planillas.Contains(Organizacion.PlanillaDeFicha[tipo]))
            return BadRequest(new { error = "Esa planilla no está activa para tu organización." });
        if (EsOrientadora && !TiposOrientacion.Contains(tipo))
            return StatusCode(403, new { error = "La orientadora solo registra fichas de orientación." });
        if ((await FichasOcultasAsync()).Contains(tipo))
            return StatusCode(403, new { error = "Esa ficha es de otra área del equipo." });

        Dictionary<string, string>? datos;
        try { datos = JsonSerializer.Deserialize<Dictionary<string, string>>(datosJson ?? "{}"); }
        catch { return BadRequest(new { error = "Datos inválidos." }); }
        if (datos == null || datos.Count == 0 || datos.Count > 30)
            return BadRequest(new { error = "La ficha no tiene datos." });
        if (datos.Any(kv => kv.Key.Length > 60 || (kv.Value?.Length ?? 0) > 4000))
            return BadRequest(new { error = "Algún campo es demasiado largo (máximo 4.000 caracteres)." });

        // Limpia espacios y descarta campos vacíos
        datos = datos.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());
        if (datos.Count == 0) return BadRequest(new { error = "Completa al menos un campo." });

        if (tipo is not ("ropa" or "tal"))   // inventario y talleres grupales no son de una sola persona
        {
            // El filtro multitenant hace que una alumna de otro colegio "no exista" para esta sesión
            if (estudianteId == null || !await _context.Estudiantes.AnyAsync(e => e.Id == estudianteId))
                return BadRequest(new { error = "Elige un alumno/a de la lista." });
        }
        else if (tipo == "ropa") estudianteId = null;

        // El correlativo lo pone el servidor (evita números repetidos si dos personas guardan a la vez)
        if (tipo == "inf") datos["N° correlativo"] = await SiguienteCorrelativoAsync();

        var reg = new RegistroPsicosocial
        {
            Tipo = tipo,
            EstudianteId = estudianteId,
            DatosJson = JsonSerializer.Serialize(datos),
            CreadoPorId = UsuarioActualId()
        };
        _context.RegistrosPsicosociales.Add(reg);
        await _context.SaveChangesAsync();

        if (tipo == "comp") await AvisarCompromisoAsync(reg.EstudianteId!.Value, datos);

        return Json(new { ok = true, id = reg.Id, correlativo = tipo == "inf" ? datos["N° correlativo"] : null });
    }

    // Un compromiso compartido avisa a quienes deben verlo (profesor/a jefe, apoderado/a, alumno/a)
    private async Task AvisarCompromisoAsync(int estudianteId, Dictionary<string, string> datos)
    {
        try
        {
            var est = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso).FirstOrDefaultAsync(e => e.Id == estudianteId);
            if (est?.Usuario == null) return;
            var vis = datos.GetValueOrDefault("Visible para", "");
            var ids = new List<int>();
            if (vis.Contains("Profesor") && est.Curso?.ProfesorJefeId != null) ids.Add(est.Curso.ProfesorJefeId.Value);
            var paraAlumno = vis.Contains("Alumno") || vis.Contains("Paciente");
            if (vis.Contains("Apoderado") || (paraAlumno && !est.AccesoPropio))   // sin cuenta propia: lo recibe su apoderado/a
                ids.AddRange(await _context.EstudiantesApoderados.Where(x => x.EstudianteId == est.Id && x.Apoderado != null).Select(x => x.Apoderado!.UsuarioId).ToListAsync());
            if (paraAlumno && est.AccesoPropio) ids.Add(est.UsuarioId);
            if (ids.Count == 0) return;
            var nombre = $"{est.Usuario.Nombre} {est.Usuario.Apellido}".Trim();
            await _notif.NotificarAsync(ids.Distinct(), "Nuevo compromiso o acuerdo",
                $"El equipo psicosocial registró un acuerdo sobre {nombre}:\n{datos.GetValueOrDefault("Compromiso o acuerdo", "")}", "/Portafolio?estudianteId=" + est.Id);
        }
        catch { /* el aviso no debe impedir guardar */ }
    }

    // POST: cambia un campo de un registro (ej.: marcar un beneficio como "Entregado")
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActualizarCampoRegistro(int id, string campo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(campo) || campo.Length > 60 || (valor?.Length ?? 0) > 4000)
            return BadRequest(new { error = "Datos inválidos." });
        var r = await _context.RegistrosPsicosociales.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if ((await FichasOcultasAsync()).Contains(r.Tipo)) return StatusCode(403, new { error = "Esa ficha es de otra área del equipo." });

        Dictionary<string, string> datos;
        try { datos = JsonSerializer.Deserialize<Dictionary<string, string>>(r.DatosJson) ?? new(); }
        catch { datos = new(); }
        if (string.IsNullOrWhiteSpace(valor)) datos.Remove(campo.Trim());
        else datos[campo.Trim()] = valor.Trim();

        r.DatosJson = JsonSerializer.Serialize(datos);
        r.ModificadoPorId = UsuarioActualId();
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST: reemplaza los campos de un registro (botón "Editar" en la ficha)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarRegistro(int id, string datosJson)
    {
        var r = await _context.RegistrosPsicosociales.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (EsOrientadora && !TiposOrientacion.Contains(r.Tipo)) return StatusCode(403, new { error = "Sin acceso a este registro." });
        if ((await FichasOcultasAsync()).Contains(r.Tipo)) return StatusCode(403, new { error = "Esa ficha es de otra área del equipo." });

        Dictionary<string, string>? datos;
        try { datos = JsonSerializer.Deserialize<Dictionary<string, string>>(datosJson ?? "{}"); }
        catch { return BadRequest(new { error = "Datos inválidos." }); }
        if (datos == null || datos.Count > 30 || datos.Any(kv => kv.Key.Length > 60 || (kv.Value?.Length ?? 0) > 4000))
            return BadRequest(new { error = "Datos inválidos." });
        datos = datos.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());
        if (datos.Count == 0) return BadRequest(new { error = "Completa al menos un campo." });

        // El correlativo de un informe no se cambia
        if (r.Tipo == "inf")
        {
            var antes = JsonSerializer.Deserialize<Dictionary<string, string>>(r.DatosJson) ?? new();
            if (antes.TryGetValue("N° correlativo", out var n)) datos["N° correlativo"] = n;
        }

        r.DatosJson = JsonSerializer.Serialize(datos);
        r.ModificadoPorId = UsuarioActualId();
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // ---------------------------------------------------------------
    // ASISTENCIA A CITACIONES
    // ---------------------------------------------------------------

    // Días sin venir para considerar a alguien "inactivo" (se le sugiere una consulta de seguimiento)
    private const int DiasInactividad = 60;

    // POST: marca si la persona asistió (Realizada) o no (NoAsistio). Solo para citas de hoy o anteriores.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarAsistencia(int id, bool asistio)
    {
        var c = await _context.Citaciones.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        if (c.Estado is EstadoCitacion.Cancelada or EstadoCitacion.Rechazada or EstadoCitacion.Solicitada or EstadoCitacion.OtraFecha)
            return BadRequest(new { error = "La citación no está agendada (cancelada, rechazada o esperando respuesta)." });
        if (c.Fecha > DateOnly.FromDateTime(DateTime.Today)) return BadRequest(new { error = "La citación todavía no ocurre." });

        c.Estado = asistio ? EstadoCitacion.Realizada : EstadoCitacion.NoAsistio;
        if (c.EventoCalendarioId != null)
        {
            var ev = await _context.EventosCalendario.FirstOrDefaultAsync(e => e.Id == c.EventoCalendarioId);
            if (ev != null) ev.Realizada = asistio;
        }
        await _context.SaveChangesAsync();
        return Json(new { ok = true, e = (int)c.Estado });
    }

    // POST: elimina un registro (borrado lógico: se conserva en la base de datos)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarRegistro(int id)
    {
        var r = await _context.RegistrosPsicosociales.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (EsOrientadora && !TiposOrientacion.Contains(r.Tipo)) return StatusCode(403, new { error = "Sin acceso a este registro." });
        if ((await FichasOcultasAsync()).Contains(r.Tipo)) return StatusCode(403, new { error = "Esa ficha es de otra área del equipo." });
        r.ModificadoPorId = UsuarioActualId();
        _context.RegistrosPsicosociales.Remove(r); // el DbContext lo convierte en IsDeleted = true
        await _context.SaveChangesAsync();
        return Json(new { ok = true });
    }
}

public record SetDto(int id, string n, string[]? it, int t, int e);
public record FilaAlumno(int Linea, string Nombre, string Rut, string Curso, string? Nacimiento, string? ApoNombre = null, string? ApoCorreo = null, string? ApoTel = null,
    string? Direccion = null, string? Telefono = null, string? Correo = null);
// d = dirección, t = teléfono, m = correo del paciente (organizaciones que no son colegio)
public record AlumnoDto(string? n, string? r, string? c, string? f, string? ap = null, string? ce = null, string? te = null,
    string? d = null, string? t = null, string? m = null);
