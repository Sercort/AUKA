using Microsoft.AspNetCore.Http;

namespace Auka.Web.Services;

/// <summary>
/// Dirección pública de AUKA para los enlaces de los correos ("Ver en AUKA", "Crea tu contraseña").
/// Si Correo:UrlBase no está configurado o apunta a localhost, se usa la dirección por la que entra la gente
/// (por ejemplo la de ngrok), que se recuerda en cada visita. Así los enlaces nunca llevan a "localhost".
/// </summary>
public static class UrlPublica
{
    private static string? _ultima;   // última dirección pública vista (sirve también para los reintentos en segundo plano)

    private static bool EsLocal(string? url) =>
        string.IsNullOrWhiteSpace(url) || url.Contains("localhost", StringComparison.OrdinalIgnoreCase) || url.Contains("127.0.0.1");

    private static string? DeLaVisita(HttpContext ctx)
    {
        var proto = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? ctx.Request.Scheme;
        var host = ctx.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? ctx.Request.Host.Value;
        if (string.IsNullOrWhiteSpace(host)) return null;
        // ngrok y otros túneles siempre son https hacia afuera
        if (host.Contains("ngrok", StringComparison.OrdinalIgnoreCase)) proto = "https";
        return $"{proto}://{host}".TrimEnd('/');
    }

    /// <summary>Se llama en cada visita: recuerda la dirección pública si no es localhost.</summary>
    public static void Recordar(HttpContext ctx)
    {
        var url = DeLaVisita(ctx);
        if (!EsLocal(url)) _ultima = url;
    }

    /// <summary>Dirección a usar en los enlaces de un correo.</summary>
    public static string Obtener(HttpContext? ctx, string? urlBase)
    {
        var cfg = (urlBase ?? "").Trim().TrimEnd('/');
        if (!EsLocal(cfg)) return cfg;                     // configurada a mano (dominio propio o ngrok fijo)
        var visita = ctx != null ? DeLaVisita(ctx) : null;
        if (!EsLocal(visita)) return visita!;
        return _ultima ?? (visita ?? cfg);                 // en el PC: la última dirección pública conocida
    }

    /// <summary>Para el panel: qué dirección se está usando y de dónde sale.</summary>
    public static (string Url, string Origen) Diagnostico(HttpContext? ctx, string? urlBase)
    {
        var cfg = (urlBase ?? "").Trim().TrimEnd('/');
        if (!EsLocal(cfg)) return (cfg, "Correo:UrlBase");
        var url = Obtener(ctx, urlBase);
        return (url, EsLocal(url) ? "localhost (los enlaces solo funcionan en tu PC: entra una vez por la dirección de ngrok)" : "dirección por la que entran (ngrok)");
    }
}
