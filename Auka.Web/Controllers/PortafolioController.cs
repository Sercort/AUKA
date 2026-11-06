using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Auka.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

public class PortafolioAlumnoVm
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Curso { get; set; } = "";
}

public class PortafolioCitacionVm
{
    public int Id { get; set; }
    public string Fecha { get; set; } = "";
    public string Hora { get; set; } = "";
    public string CitadoA { get; set; } = "";
    public string Motivo { get; set; } = "";
    public EstadoCitacion Estado { get; set; }
    public string EstadoTexto { get; set; } = "";
    public bool PuedeConfirmar { get; set; }      // compatibilidad: igual a PuedeResponder con estado Pendiente
    public bool PuedeResponder { get; set; }      // confirmar, proponer otra fecha o rechazar
    public bool PuedeAtender { get; set; }        // profesor/a jefe: aceptar o rechazar una cita que le pidieron
    public bool EsPasada { get; set; }
    public bool EsSolicitud { get; set; }         // la pidió el alumno/a o el apoderado/a
    public string Destinatario { get; set; } = "";
    public string Respuesta { get; set; } = "";
    public string FechaPropuesta { get; set; } = "";
}

public class PortafolioAnotacionVm
{
    public string Fecha { get; set; } = "";
    public bool Positiva { get; set; }
    public string Detalle { get; set; } = "";
}

public class PortafolioCompromisoVm
{
    public string Fecha { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Revision { get; set; } = "";
}

public class PortafolioVm
{
    public bool EsApoderado { get; set; }
    public bool EsDocente { get; set; }
    public bool AccesoPropio { get; set; } = true;   // false: el alumno/a no usa la plataforma (kínder a 6° básico)
    public List<PortafolioAlumnoVm> Alumnos { get; set; } = new();
    public PortafolioAlumnoVm? Actual { get; set; }
    public List<PortafolioCitacionVm> Proximas { get; set; } = new();
    public List<PortafolioCitacionVm> Historial { get; set; } = new();
    public List<PortafolioCitacionVm> Citaciones => Proximas.Concat(Historial).ToList();
    public List<PortafolioCompromisoVm> Compromisos { get; set; } = new();
    public List<PortafolioAnotacionVm> Anotaciones { get; set; } = new();
    public int Positivas { get; set; }
    public int Negativas { get; set; }
    public string[] Destinatarios { get; set; } = Array.Empty<string>();
    public string? Mensaje { get; set; }
}

/// <summary>
/// Portafolio psicosocial de alumnos/as, apoderados/as y profesores/as jefe: citaciones (responder, pedir una cita),
/// compromisos que el equipo comparte y anotaciones de conducta. No muestra fichas ni bitácoras confidenciales,
/// ni cursos, notas, clubes u horario (eso es del Plan Premium).
/// </summary>
[Authorize(Policy = "FamiliaPsicosocial")]
public class PortafolioController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly INotificacionService _notif;

    public PortafolioController(ApplicationDbContext context, INotificacionService notif)
    {
        _context = context;
        _notif = notif;
    }

    private int UsuarioActualId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    private bool EsApoderado => User.FindFirst(ClaimTypes.Role)?.Value == "Apoderado";
    private bool EsDocente => User.FindFirst(ClaimTypes.Role)?.Value == "Docente";
    private static string F(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    private static string H(TimeOnly? h) => h.HasValue ? h.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "";

    private async Task<List<PortafolioAlumnoVm>> MisAlumnosAsync()
    {
        var uid = UsuarioActualId();
        List<Estudiante> ests;
        if (EsDocente)
        {
            // Profesor/a jefe: los alumnos/as de los cursos que tiene a su cargo
            ests = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.Curso != null && e.Curso.ProfesorJefeId == uid)
                .ToListAsync();
        }
        else if (EsApoderado)
        {
            // Estudiantes tiene filtro por colegio: nunca aparece un alumno/a de otro colegio
            ests = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.Apoderados.Any(x => x.Apoderado != null && x.Apoderado.UsuarioId == uid))
                .ToListAsync();
        }
        else
        {
            ests = await _context.Estudiantes.AsNoTracking()
                .Include(e => e.Usuario).Include(e => e.Curso)
                .Where(e => e.UsuarioId == uid).ToListAsync();
        }
        return ests.Where(e => e.Usuario != null).GroupBy(e => e.Id).Select(g => g.First())
            .OrderBy(e => e.Usuario!.Nombre)
            .Select(e => new PortafolioAlumnoVm
            {
                Id = e.Id,
                Nombre = $"{e.Usuario!.Nombre} {e.Usuario.Apellido}".Trim(),
                Curso = e.Curso == null || e.Curso.Nombre == "Pacientes" ? "" : (string.IsNullOrEmpty(e.Curso.Letra) ? e.Curso.Nombre : $"{e.Curso.Nombre} {e.Curso.Letra}")
            }).ToList();
    }

    // GET: /Portafolio
    [HttpGet]
    public async Task<IActionResult> Index(int? estudianteId, string? ok)
    {
        var esColegio = (ViewData["Org"] as DatosOrganizacion)?.V.EsColegio ?? true;
        var vm = new PortafolioVm
        {
            EsApoderado = EsApoderado,
            EsDocente = EsDocente,
            Alumnos = await MisAlumnosAsync(),
            // Con quién se puede pedir una cita (fuera de colegios no hay profesor/a jefe)
            Destinatarios = Citaciones.Destinatarios.Where(d => esColegio || d != "Profesor/a jefe").ToArray(),
            Mensaje = ok
        };
        vm.Actual = vm.Alumnos.FirstOrDefault(a => a.Id == estudianteId) ?? vm.Alumnos.FirstOrDefault();
        if (vm.Actual == null) return View(vm);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var citas = await _context.Citaciones.AsNoTracking()
            .Where(c => c.EstudianteId == vm.Actual.Id && c.Estado != EstadoCitacion.Cancelada)
            .OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Hora).Take(60).ToListAsync();

        // Alumno/a sin cuenta propia (kínder a 6° básico): su apoderado/a ve y responde todo por él/ella.
        // Con cuenta propia: cada uno ve lo suyo y lo del otro solo si el equipo marcó "informar también".
        var acceso = await _context.Estudiantes.AsNoTracking().Where(e => e.Id == vm.Actual.Id).Select(e => e.AccesoPropio).FirstOrDefaultAsync();
        vm.AccesoPropio = acceso;
        var visibles = citas.Where(c => Citaciones.LaVe(c, EsApoderado, EsDocente, acceso));

        var lista = visibles.Select(c => new PortafolioCitacionVm
        {
            Id = c.Id,
            Fecha = F(c.Fecha),
            Hora = H(c.Hora),
            CitadoA = c.CitadoA,
            Motivo = c.Motivo,
            Estado = c.Estado,
            EstadoTexto = Citaciones.Estado(c.Estado),
            PuedeResponder = c.Fecha >= hoy && c.Estado is EstadoCitacion.Pendiente && Citaciones.PuedeResponder(c, EsApoderado, EsDocente, acceso),
            PuedeAtender = EsDocente && c.Estado == EstadoCitacion.Solicitada && c.Destinatario == "Profesor/a jefe",
            EsPasada = c.Fecha < hoy || c.Estado is EstadoCitacion.Realizada or EstadoCitacion.NoAsistio,
            EsSolicitud = c.SolicitadaPorId != null,
            Destinatario = c.Destinatario ?? "",
            Respuesta = c.Respuesta ?? "",
            FechaPropuesta = c.FechaPropuesta.HasValue ? F(c.FechaPropuesta.Value) + (c.HoraPropuesta.HasValue ? " " + H(c.HoraPropuesta) : "") : ""
        }).ToList();
        lista.ForEach(c => c.PuedeConfirmar = c.PuedeResponder);
        vm.Proximas = lista.Where(c => !c.EsPasada).OrderBy(c => DateOnly.ParseExact(c.Fecha, "dd-MM-yyyy", CultureInfo.InvariantCulture)).ToList();
        vm.Historial = lista.Where(c => c.EsPasada).ToList();

        // Compromisos y notas que el equipo decidió compartir con este rol
        var comps = await _context.RegistrosPsicosociales.AsNoTracking()
            .Where(r => r.EstudianteId == vm.Actual.Id && r.Tipo == "comp")
            .OrderByDescending(r => r.FechaUtc).Take(20).ToListAsync();
        foreach (var r in comps)
        {
            Dictionary<string, string> d;
            try { d = JsonSerializer.Deserialize<Dictionary<string, string>>(r.DatosJson) ?? new(); } catch { d = new(); }
            var vis = d.GetValueOrDefault("Visible para", "");
            var paraAlumno = vis.Contains("Alumno") || vis.Contains("Paciente");
            var loVe = EsDocente ? vis.Contains("Profesor") : EsApoderado ? vis.Contains("Apoderado") || (!acceso && paraAlumno) : paraAlumno;
            if (!loVe) continue;
            vm.Compromisos.Add(new PortafolioCompromisoVm
            {
                Fecha = DateOnly.TryParse(d.GetValueOrDefault("Fecha", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fc) ? F(fc) : r.FechaUtc.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                Texto = d.GetValueOrDefault("Compromiso o acuerdo", ""),
                Tipo = d.GetValueOrDefault("Tipo", "Compromiso"),
                Revision = DateOnly.TryParse(d.GetValueOrDefault("Fecha de revisión", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fr) ? F(fr) : ""
            });
        }

        // Solo anotaciones de conducta del propio alumno/a (las clínicas del equipo nunca se muestran aquí)
        var anots = await _context.Anotaciones.AsNoTracking()
            .Where(a => a.EstudianteId == vm.Actual.Id)
            .OrderByDescending(a => a.Fecha).Take(30).ToListAsync();
        vm.Anotaciones = anots.Select(a => new PortafolioAnotacionVm
        {
            Fecha = a.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Positiva = (int)a.Tipo == 1,
            Detalle = a.Detalle
        }).ToList();
        vm.Positivas = vm.Anotaciones.Count(a => a.Positiva);
        vm.Negativas = vm.Anotaciones.Count(a => !a.Positiva);
        return View(vm);
    }

    // A quién avisar del lado del equipo: quien citó y los profesionales que participan
    private static IEnumerable<int> EquipoDe(Citacion c) => Citaciones.Participantes(c.Participantes).Append(c.CreadaPorId).Distinct();

    private async Task<Citacion?> CitacionMiaAsync(int id)
    {
        var cit = await _context.Citaciones.Include(c => c.Estudiante).ThenInclude(e => e!.Usuario).FirstOrDefaultAsync(c => c.Id == id);
        if (cit == null) return null;
        var mios = (await MisAlumnosAsync()).Any(a => a.Id == cit.EstudianteId);
        return mios ? cit : null;
    }

    // POST: /Portafolio/Confirmar (se mantiene por compatibilidad)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Confirmar(int id) => Responder(id, "confirmar", null, null, null);

    // POST: /Portafolio/Responder -> confirmar, proponer otra fecha o rechazar (con motivo)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Responder(int id, string accion, string? comentario, string? fecha, string? hora)
    {
        var cit = await CitacionMiaAsync(id);
        if (cit == null) return Forbid();
        var accesoPropio = await _context.Estudiantes.AsNoTracking().Where(e => e.Id == cit.EstudianteId).Select(e => e.AccesoPropio).FirstOrDefaultAsync();
        if (!Citaciones.PuedeResponder(cit, EsApoderado, EsDocente, accesoPropio)) return Forbid();   // solo responde quien fue citado/a
        if (cit.Estado != EstadoCitacion.Pendiente)
            return RedirectToAction(nameof(Index), new { estudianteId = cit.EstudianteId, ok = "Esta citación ya fue respondida." });

        comentario = (comentario ?? "").Trim();
        if (comentario.Length > 300) comentario = comentario[..300];
        var quien = User.Identity?.Name ?? "Un/a usuario/a";
        var nombre = cit.Estudiante?.Usuario == null ? "el alumno/a" : $"{cit.Estudiante.Usuario.Nombre} {cit.Estudiante.Usuario.Apellido}".Trim();
        string titulo, mensaje, ok;

        if (accion == "confirmar")
        {
            cit.Estado = EstadoCitacion.Confirmada;
            titulo = "Citación confirmada";
            mensaje = $"{quien} confirmó su asistencia a la citación del {F(cit.Fecha)} ({nombre}).";
            ok = "¡Listo! Confirmaste tu asistencia.";
        }
        else if (accion == "otra")
        {
            if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fp) || fp < DateOnly.FromDateTime(DateTime.Today))
                return RedirectToAction(nameof(Index), new { estudianteId = cit.EstudianteId, ok = "Elige una fecha desde hoy para proponerla." });
            cit.Estado = EstadoCitacion.OtraFecha;
            cit.FechaPropuesta = fp;
            cit.HoraPropuesta = TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hp) ? hp : null;
            cit.Respuesta = comentario.Length > 0 ? comentario : null;
            titulo = "Proponen otra fecha para una citación";
            mensaje = $"{quien} no puede el {F(cit.Fecha)} ({nombre}) y propone el {F(fp)}" + (cit.HoraPropuesta.HasValue ? $" a las {H(cit.HoraPropuesta)}" : "") + "." +
                      (comentario.Length > 0 ? $"\nComentario: {comentario}" : "") + "\nRespóndele desde tu panel en AUKA.";
            ok = "Enviamos tu propuesta. Te avisaremos cuando la respondan.";
        }
        else if (accion == "rechazar")
        {
            if (comentario.Length < 3)
                return RedirectToAction(nameof(Index), new { estudianteId = cit.EstudianteId, ok = "Cuéntanos brevemente por qué no puedes asistir." });
            cit.Estado = EstadoCitacion.Rechazada;
            cit.Respuesta = comentario;
            titulo = "Citación rechazada";
            mensaje = $"{quien} no asistirá a la citación del {F(cit.Fecha)} ({nombre}).\nMotivo: {comentario}";
            ok = "Avisamos al equipo que no podrás asistir.";
        }
        else return BadRequest();

        cit.ConfirmadaEnUtc = DateTime.UtcNow;
        cit.ConfirmadaPorId = UsuarioActualId();
        await _context.SaveChangesAsync();
        try { await _notif.NotificarAsync(EquipoDe(cit), titulo, mensaje, "/Psychosocial"); } catch { }
        return RedirectToAction(nameof(Index), new { estudianteId = cit.EstudianteId, ok });
    }

    // POST: /Portafolio/PedirCita -> el alumno/a o el apoderado/a pide una cita
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PedirCita(int estudianteId, string destinatario, string fecha, string? hora, string? motivo)
    {
        if (EsDocente) return Forbid();
        var est = await _context.Estudiantes.Include(e => e.Usuario).Include(e => e.Curso).FirstOrDefaultAsync(e => e.Id == estudianteId);
        if (est?.Usuario == null || !(await MisAlumnosAsync()).Any(a => a.Id == estudianteId)) return Forbid();
        if (!Citaciones.Destinatarios.Contains(destinatario))
            return RedirectToAction(nameof(Index), new { estudianteId, ok = "Elige con quién quieres la cita." });
        if (!DateOnly.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) || f < DateOnly.FromDateTime(DateTime.Today))
            return RedirectToAction(nameof(Index), new { estudianteId, ok = "Elige una fecha desde hoy." });
        motivo = (motivo ?? "").Trim();
        if (motivo.Length > 300) motivo = motivo[..300];

        // Evita repetir: una solicitud abierta por persona y destinatario
        var yo = UsuarioActualId();
        if (await _context.Citaciones.AnyAsync(c => c.EstudianteId == est.Id && c.SolicitadaPorId == yo && c.Destinatario == destinatario && c.Estado == EstadoCitacion.Solicitada))
            return RedirectToAction(nameof(Index), new { estudianteId, ok = "Ya tienes una solicitud abierta con esa persona. Te avisaremos cuando la agenden." });

        var cit = new Citacion
        {
            EstudianteId = est.Id,
            CitadoA = EsApoderado ? "Apoderado" : (est.Curso?.Nombre == "Pacientes" ? "Paciente" : "Alumno"),
            Fecha = f,
            Hora = TimeOnly.TryParse(hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : null,
            Motivo = motivo.Length > 0 ? motivo : "Solicitud de entrevista",
            Estado = EstadoCitacion.Solicitada,
            Destinatario = destinatario,
            SolicitadaPorId = yo,
            CreadaPorId = yo
        };
        _context.Citaciones.Add(cit);
        await _context.SaveChangesAsync();

        // Aviso a quien atiende: por rol del equipo, o el profesor/a jefe del curso
        var ids = new List<int>();
        if (destinatario == "Profesor/a jefe")
        {
            if (est.Curso?.ProfesorJefeId != null) ids.Add(est.Curso.ProfesorJefeId.Value);
        }
        else
        {
            var roles = Citaciones.RolesDe(destinatario);
            var tenant = _context.CurrentTenantId;
            ids = await _context.Usuarios.AsNoTracking().Where(u => u.ColegioId == tenant && u.Activo && roles.Contains(u.Rol)).Select(u => u.Id).ToListAsync();
        }
        var quien = User.Identity?.Name ?? "Un/a usuario/a";
        var nombre = $"{est.Usuario.Nombre} {est.Usuario.Apellido}".Trim();
        try
        {
            await _notif.NotificarAsync(ids, "Te pidieron una cita",
                $"{quien} pide una cita ({nombre}) para el {F(f)}" + (cit.Hora.HasValue ? $" a las {H(cit.Hora)}" : "") + "." +
                (motivo.Length > 0 ? $"\nMotivo: {motivo}" : "") + "\nAgéndala o respóndela desde AUKA.",
                destinatario == "Profesor/a jefe" ? "/Portafolio?estudianteId=" + est.Id : "/Psychosocial");
        }
        catch { }
        return RedirectToAction(nameof(Index), new { estudianteId, ok = "Enviamos tu solicitud. Te avisaremos cuando la agenden." });
    }

    // POST: /Portafolio/AtenderSolicitud -> el profesor/a jefe acepta o rechaza una cita que le pidieron
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtenderSolicitud(int id, bool aceptar, string? comentario)
    {
        if (!EsDocente) return Forbid();
        var cit = await CitacionMiaAsync(id);
        if (cit == null || cit.Estado != EstadoCitacion.Solicitada || cit.Destinatario != "Profesor/a jefe") return Forbid();
        comentario = (comentario ?? "").Trim();
        if (comentario.Length > 300) comentario = comentario[..300];

        cit.Estado = aceptar ? EstadoCitacion.Confirmada : EstadoCitacion.Cancelada;
        cit.Respuesta = comentario.Length > 0 ? comentario : null;
        cit.ConfirmadaEnUtc = DateTime.UtcNow;
        cit.ConfirmadaPorId = UsuarioActualId();
        await _context.SaveChangesAsync();

        var quien = User.Identity?.Name ?? "El profesor/a jefe";
        try
        {
            if (cit.SolicitadaPorId.HasValue)
                await _notif.NotificarAsync(new[] { cit.SolicitadaPorId.Value }, aceptar ? "Tu cita fue aceptada" : "Tu solicitud de cita",
                    aceptar ? $"{quien} aceptó la cita del {F(cit.Fecha)}" + (cit.Hora.HasValue ? $" a las {H(cit.Hora)}" : "") + "." + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "")
                            : $"{quien} no puede en esa fecha." + (comentario.Length > 0 ? $"\nComentario: {comentario}" : "") + " Puedes pedir otra.",
                    "/Portafolio?estudianteId=" + cit.EstudianteId);
        }
        catch { }
        return RedirectToAction(nameof(Index), new { estudianteId = cit.EstudianteId, ok = aceptar ? "Aceptaste la cita." : "Rechazaste la solicitud." });
    }
}
