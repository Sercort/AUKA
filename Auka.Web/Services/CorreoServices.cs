using System.Net;
using Auka.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Auka.Web.Services;

/// <summary>Configuración del correo (sección "Correo" de appsettings.json / secretos de usuario).</summary>
public class CorreoOptions
{
    public bool Habilitado { get; set; } = true;           // true = envía correos reales. Poner false solo para pruebas (simula)
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

        // Gmail solo deja enviar "desde" la misma cuenta con que se inicia sesión
        var remitente = string.IsNullOrWhiteSpace(_opt.Remitente) ||
                        (_opt.Servidor.Contains("gmail", StringComparison.OrdinalIgnoreCase) && !string.Equals(_opt.Remitente, _opt.Usuario, StringComparison.OrdinalIgnoreCase))
            ? _opt.Usuario : _opt.Remitente;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_opt.NombreRemitente, remitente));
        foreach (var d in lista)
        {
            if (!MailboxAddress.TryParse(d.Trim(), out var dir)) return (false, false, $"Correo de destino no válido: {d}");
            msg.To.Add(dir);
        }
        msg.Subject = asunto;
        msg.Body = new BodyBuilder { HtmlBody = cuerpoHtml }.ToMessageBody();

        // Envío con MailKit (más estable con Gmail que System.Net.Mail).
        // Primero el puerto configurado (587 con STARTTLS); si la conexión se corta, se reintenta por 465 (SSL directo).
        var intentos = new List<(int Puerto, SecureSocketOptions Seg)>
        {
            (_opt.Puerto, _opt.Puerto == 465 ? SecureSocketOptions.SslOnConnect : _opt.UsarSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto)
        };
        if (_opt.Puerto == 587 && _opt.Servidor.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            intentos.Add((465, SecureSocketOptions.SslOnConnect));

        string? motivo = null;
        foreach (var (puerto, seg) in intentos)
        {
            try
            {
                using var cliente = new SmtpClient { Timeout = 20000 };
                await cliente.ConnectAsync(_opt.Servidor, puerto, seg, ct);
                await cliente.AuthenticateAsync(_opt.Usuario, _opt.Clave, ct);
                await cliente.SendAsync(msg, ct);
                await cliente.DisconnectAsync(true, ct);
                return (true, false, null);
            }
            catch (AuthenticationException ex)
            {
                // La clave no sirve: no tiene sentido probar otro puerto
                _log.LogWarning(ex, "Gmail rechazó la clave de {Usuario}", _opt.Usuario);
                return (false, false, "Gmail no aceptó la clave (5.7.8 Username and Password not accepted): usa una contraseña de aplicación de 16 letras de la cuenta " + _opt.Usuario + ". " + ex.Message);
            }
            catch (SslHandshakeException ex)
            {
                _log.LogWarning(ex, "Error de conexión segura con {Servidor}:{Puerto}", _opt.Servidor, puerto);
                motivo = $"Puerto {puerto}: no se pudo crear la conexión segura (secure connection). Suele ser un antivirus que revisa el correo: desactiva su protección de correo. {ex.Message}";
            }
            catch (SmtpCommandException ex)
            {
                _log.LogWarning(ex, "Gmail rechazó el envío a {Destinos}", string.Join(", ", lista));
                return (false, false, $"Gmail rechazó el envío ({(int)ex.StatusCode} {ex.StatusCode}): {ex.Message}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo enviar el correo por {Servidor}:{Puerto}", _opt.Servidor, puerto);
                var m = ex.Message;
                for (var e = ex.InnerException; e != null; e = e.InnerException) m += " → " + e.Message;
                motivo = (motivo == null ? "" : motivo + " | ") + $"Puerto {puerto}: {m}";
            }
        }
        return (false, false, motivo);
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
