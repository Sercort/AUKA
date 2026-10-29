using System.Net;
using System.Net.Mail;
using Auka.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Auka.Web.Services;

/// <summary>Configuración del correo (sección "Correo" de appsettings.json / secretos de usuario).</summary>
public class CorreoOptions
{
    public bool Habilitado { get; set; } = false;          // false = modo pruebas: no envía, solo simula
    public string Servidor { get; set; } = "smtp.gmail.com";
    public int Puerto { get; set; } = 587;
    public bool UsarSsl { get; set; } = true;
    public string Usuario { get; set; } = "";
    public string Clave { get; set; } = "";                 // contraseña de aplicación (no la clave normal)
    public string Remitente { get; set; } = "";             // si está vacío se usa Usuario
    public string NombreRemitente { get; set; } = "AUKA";
    public string UrlBase { get; set; } = "";               // ej: https://localhost:7157 (para los enlaces del correo)
}

public class SmtpCorreoService : ICorreoService
{
    private readonly CorreoOptions _opt;
    private readonly ILogger<SmtpCorreoService> _log;

    public SmtpCorreoService(IOptions<CorreoOptions> opt, ILogger<SmtpCorreoService> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public async Task<(bool Ok, bool Simulado, string? Error)> EnviarAsync(
        IEnumerable<string> destinos, string asunto, string cuerpoHtml, CancellationToken ct = default)
    {
        var lista = destinos.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
        if (lista.Count == 0) return (false, false, "Sin destinatarios.");

        if (!_opt.Habilitado)
        {
            _log.LogInformation("CORREO SIMULADO a {Destinos}: {Asunto}", string.Join(", ", lista), asunto);
            return (true, true, null);
        }

        try
        {
            using var cliente = new SmtpClient(_opt.Servidor, _opt.Puerto)
            {
                EnableSsl = _opt.UsarSsl,
                Credentials = new NetworkCredential(_opt.Usuario, _opt.Clave),
                Timeout = 15000
            };
            var remitente = string.IsNullOrWhiteSpace(_opt.Remitente) ? _opt.Usuario : _opt.Remitente;
            using var msg = new MailMessage
            {
                From = new MailAddress(remitente, _opt.NombreRemitente),
                Subject = asunto,
                Body = cuerpoHtml,
                IsBodyHtml = true,
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8
            };
            foreach (var d in lista) msg.To.Add(d);
            await cliente.SendMailAsync(msg, ct);
            return (true, false, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo enviar el correo a {Destinos}", string.Join(", ", lista));
            return (false, false, ex.Message);
        }
    }
}

public static class CorreoPlantilla
{
    public static string Construir(string titulo, string mensaje, string? url, string colegio)
    {
        var t = WebUtility.HtmlEncode(titulo);
        var m = WebUtility.HtmlEncode(mensaje).Replace("\n", "<br>");
        var boton = string.IsNullOrEmpty(url) ? "" :
            $"<p style=\"margin:22px 0\"><a href=\"{WebUtility.HtmlEncode(url)}\" style=\"background:#2459b0;color:#fff;text-decoration:none;padding:11px 22px;border-radius:999px;font-weight:bold;display:inline-block\">Ver en AUKA</a></p>";
        var cole = string.IsNullOrWhiteSpace(colegio) ? "" : $"<div style=\"color:#5d6f86;font-size:13px;margin-top:4px\">{WebUtility.HtmlEncode(colegio)}</div>";
        return $@"<div style=""font-family:Arial,Helvetica,sans-serif;background:#f3f7fc;padding:24px"">
<div style=""max-width:560px;margin:0 auto;background:#fff;border-radius:16px;overflow:hidden;border:1px solid #e3eaf4"">
<div style=""background:linear-gradient(120deg,#1a319b,#2459b0 60%,#1fb5c5);color:#fff;padding:18px 24px""><b style=""font-size:20px;letter-spacing:1px"">AUKA</b>{cole}</div>
<div style=""padding:24px;color:#14233a""><h2 style=""margin:0 0 12px;font-size:18px"">{t}</h2><div style=""font-size:15px;line-height:1.5"">{m}</div>{boton}
<div style=""color:#5d6f86;font-size:12px;border-top:1px solid #e3eaf4;padding-top:12px"">Este es un aviso automático de AUKA. Si no esperabas este mensaje, puedes ignorarlo.</div></div></div></div>";
    }
}
