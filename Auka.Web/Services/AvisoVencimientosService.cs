using Auka.Application.Entities;
using Auka.Application.Interfaces;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auka.Web.Services;

/// <summary>
/// Una vez al día (desde las 08:00, hora de Chile) revisa qué colegios vencen en 7 días o menos (o ya vencieron
/// hace menos de 30 días) y le manda un resumen por correo al dueño de la plataforma. Si no hay nada, no envía nada.
/// </summary>
public class AvisoVencimientosService : BackgroundService
{
    private const string Clave = "AvisoVencimientos";
    private const int DiasAviso = 7;
    private const int DiasVencidoMax = 30;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AvisoVencimientosService> _log;

    public AvisoVencimientosService(IServiceScopeFactory scopes, ILogger<AvisoVencimientosService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RevisarAsync(stoppingToken); }
            catch (Exception ex) { _log.LogWarning(ex, "Falló el aviso de vencimientos."); }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (TaskCanceledException) { break; }
        }
    }

    private static TimeZoneInfo ZonaChile()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* prueba el siguiente */ }
        }
        return TimeZoneInfo.Utc;
    }

    private async Task RevisarAsync(CancellationToken ct)
    {
        var zona = ZonaChile();
        var ahora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zona);
        if (ahora.Hour < 8) return;
        var hoy = ahora.Date;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var correo = scope.ServiceProvider.GetRequiredService<ICorreoService>();
        var opt = scope.ServiceProvider.GetRequiredService<IOptions<CorreoOptions>>().Value;

        // ¿Ya se revisó hoy?
        var reg = await db.RegistrosTarea.FirstOrDefaultAsync(r => r.Clave == Clave, ct);
        if (reg != null && TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reg.UltimaEjecucionUtc, DateTimeKind.Utc), zona).Date >= hoy) return;

        // Sin sesión web: se ignoran los filtros de colegio a propósito
        var colegios = await db.Colegios.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Activo && c.FechaVencimiento != null).ToListAsync(ct);

        var items = colegios
            .Select(c => new { c, dias = (c.FechaVencimiento!.Value.Date - hoy).Days })
            .Where(x => x.dias <= DiasAviso && x.dias >= -DiasVencidoMax)
            .OrderBy(x => x.dias).ToList();

        var enviado = true;
        if (items.Count > 0)
        {
            var duenos = await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Rol == RolUsuario.AdminPlataforma && u.Activo).ToListAsync(ct);
            var destinos = duenos.SelectMany(NotificacionService.Destinos).Distinct().ToList();

            if (destinos.Count > 0)
            {
                string Linea(string nombre, int dias, DateTime vence) =>
                    "• " + nombre + " — " + (dias < 0 ? $"venció hace {-dias} d" : dias == 0 ? "vence HOY" : $"vence en {dias} d") + $" ({vence:dd-MM-yyyy})";

                var vencidos = items.Where(x => x.dias < 0).Select(x => Linea(x.c.Nombre, x.dias, x.c.FechaVencimiento!.Value)).ToList();
                var porVencer = items.Where(x => x.dias >= 0).Select(x => Linea(x.c.Nombre, x.dias, x.c.FechaVencimiento!.Value)).ToList();

                var partes = new List<string>();
                if (porVencer.Count > 0) partes.Add("POR VENCER (próximos " + DiasAviso + " días):\n" + string.Join("\n", porVencer));
                if (vencidos.Count > 0) partes.Add("YA VENCIDOS:\n" + string.Join("\n", vencidos));

                var url = string.IsNullOrWhiteSpace(opt.UrlBase) ? null : opt.UrlBase.TrimEnd('/') + "/Plataforma/Colegios";
                var titulo = items.Count == 1 ? "1 colegio con vencimiento cercano" : items.Count + " colegios con vencimiento cercano";
                var html = CorreoPlantilla.Construir(titulo, string.Join("\n\n", partes), url, "Resumen diario · panel de plataforma");
                var r = await correo.EnviarAsync(destinos, "AUKA · " + titulo, html, ct);
                enviado = r.Ok;
            }
            else
            {
                _log.LogWarning("Hay colegios por vencer pero no hay correo del dueño de la plataforma para avisarle.");
            }
        }

        // Si el correo falló no se marca como hecho: se reintenta en la próxima hora
        if (!enviado) return;
        if (reg == null) db.RegistrosTarea.Add(new RegistroTarea { Clave = Clave, UltimaEjecucionUtc = DateTime.UtcNow });
        else reg.UltimaEjecucionUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
