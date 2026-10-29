using Auka.Application.Entities;
using Auka.Application.Interfaces;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auka.Web.Services;

public class ResultadoNotificacion
{
    public int Total { get; set; }
    public int Enviados { get; set; }
    public int Simulados { get; set; }
    public int Fallidos { get; set; }
    public int SinCorreo { get; set; }
}

public interface INotificacionService
{
    /// <summary>Crea el aviso dentro de la plataforma e intenta enviarlo al correo de cada persona.</summary>
    Task<ResultadoNotificacion> NotificarAsync(IEnumerable<int> usuarioIds, string titulo, string mensaje, string? enlace = null);
}

public class NotificacionService : INotificacionService
{
    private const string DominioInterno = "@alumnos.auka.cl";   // correos de alumnos/as que no existen de verdad

    private readonly ApplicationDbContext _context;
    private readonly ICorreoService _correo;
    private readonly IHttpContextAccessor _http;
    private readonly CorreoOptions _opt;

    public NotificacionService(ApplicationDbContext context, ICorreoService correo, IHttpContextAccessor http, IOptions<CorreoOptions> opt)
    {
        _context = context;
        _correo = correo;
        _http = http;
        _opt = opt.Value;
    }

    /// <summary>Correos reales de la persona: el de ingreso (si no es el interno) y el personal.</summary>
    public static List<string> Destinos(Usuario u)
    {
        var l = new List<string>();
        void Agregar(string? c)
        {
            c = c?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(c) && c.Contains('@') && !c.EndsWith(DominioInterno) && !l.Contains(c)) l.Add(c);
        }
        Agregar(u.Email);
        Agregar(u.EmailPersonal);
        return l;
    }

    public async Task<ResultadoNotificacion> NotificarAsync(IEnumerable<int> usuarioIds, string titulo, string mensaje, string? enlace = null)
    {
        var res = new ResultadoNotificacion();
        var ids = usuarioIds.Distinct().ToList();
        if (ids.Count == 0) return res;

        // El filtro multitenant limita la búsqueda al colegio de la sesión
        var usuarios = await _context.Usuarios.Where(u => ids.Contains(u.Id) && u.Activo).ToListAsync();
        var ctx = _http.HttpContext;
        var colegio = ctx?.User.FindFirst("ColegioNombre")?.Value ?? "";
        var baseUrl = !string.IsNullOrWhiteSpace(_opt.UrlBase) ? _opt.UrlBase.TrimEnd('/')
                    : ctx != null ? $"{ctx.Request.Scheme}://{ctx.Request.Host}" : "";

        var pares = new List<(Notificacion n, List<string> destinos)>();
        foreach (var u in usuarios)
        {
            var destinos = Destinos(u);
            var n = new Notificacion
            {
                UsuarioId = u.Id,
                Titulo = titulo,
                Mensaje = mensaje,
                Enlace = enlace,
                CorreoDestino = string.Join(";", destinos),
                EstadoCorreo = destinos.Count == 0 ? EstadoEnvioCorreo.SinCorreo : EstadoEnvioCorreo.Pendiente
            };
            _context.Notificaciones.Add(n);
            pares.Add((n, destinos));
        }
        await _context.SaveChangesAsync();

        foreach (var (n, destinos) in pares)
        {
            if (destinos.Count == 0) { res.SinCorreo++; continue; }
            var url = string.IsNullOrEmpty(enlace) ? null : baseUrl + enlace;
            var r = await _correo.EnviarAsync(destinos, titulo, CorreoPlantilla.Construir(titulo, mensaje, url, colegio));
            n.IntentosCorreo++;
            if (r.Ok)
            {
                n.EstadoCorreo = r.Simulado ? EstadoEnvioCorreo.Simulado : EstadoEnvioCorreo.Enviado;
                if (r.Simulado) res.Simulados++; else res.Enviados++;
            }
            else
            {
                n.EstadoCorreo = EstadoEnvioCorreo.Fallido;
                n.UltimoErrorCorreo = r.Error != null && r.Error.Length > 300 ? r.Error[..300] : r.Error;
                res.Fallidos++;
            }
        }
        await _context.SaveChangesAsync();
        res.Total = usuarios.Count;
        return res;
    }
}

/// <summary>Cada 5 minutos reintenta los correos que fallaron (hasta 5 intentos por aviso).</summary>
public class ReintentoCorreosService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReintentoCorreosService> _log;

    public ReintentoCorreosService(IServiceScopeFactory scopes, ILogger<ReintentoCorreosService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (TaskCanceledException) { break; }

            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var correo = scope.ServiceProvider.GetRequiredService<ICorreoService>();
                var opt = scope.ServiceProvider.GetRequiredService<IOptions<CorreoOptions>>().Value;

                // Sin sesión web: se ignoran los filtros de colegio a propósito
                var fallidos = await db.Notificaciones.IgnoreQueryFilters()
                    .Where(n => n.EstadoCorreo == EstadoEnvioCorreo.Fallido && n.IntentosCorreo < 5)
                    .OrderBy(n => n.Id).Take(50).ToListAsync(stoppingToken);

                foreach (var n in fallidos)
                {
                    var destinos = (n.CorreoDestino ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                    var url = string.IsNullOrEmpty(n.Enlace) || string.IsNullOrWhiteSpace(opt.UrlBase) ? null : opt.UrlBase.TrimEnd('/') + n.Enlace;
                    var r = await correo.EnviarAsync(destinos, n.Titulo, CorreoPlantilla.Construir(n.Titulo, n.Mensaje, url, ""), stoppingToken);
                    n.IntentosCorreo++;
                    if (r.Ok) n.EstadoCorreo = r.Simulado ? EstadoEnvioCorreo.Simulado : EstadoEnvioCorreo.Enviado;
                    else n.UltimoErrorCorreo = r.Error != null && r.Error.Length > 300 ? r.Error[..300] : r.Error;
                }
                if (fallidos.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falló el reintento de correos.");
            }
        }
    }
}
