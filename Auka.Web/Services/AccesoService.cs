using System.Security.Cryptography;
using System.Text;
using Auka.Application.Entities;
using Auka.Application.Interfaces;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auka.Web.Services;

/// <summary>Resultado del envío del correo de acceso.</summary>
public record ResultadoAcceso(bool Enviado, bool Simulado, string? Motivo);

public interface IAccesoService
{
    /// <summary>
    /// Envía al correo real de la persona un enlace para que cree su propia contraseña (sirve una vez, dura 72 horas).
    /// No se envía ninguna clave por correo: la persona la crea ella misma.
    /// </summary>
    Task<ResultadoAcceso> EnviarInvitacionAsync(Usuario u, string baseUrl, string organizacion);
}

public class AccesoService : IAccesoService
{
    public const int HorasInvitacion = 72;

    private readonly ApplicationDbContext _context;
    private readonly ICorreoService _correo;
    private readonly CorreoOptions _opt;

    public AccesoService(ApplicationDbContext context, ICorreoService correo, IOptions<CorreoOptions> opt)
    {
        _context = context;
        _correo = correo;
        _opt = opt.Value;
    }

    // Mismo cálculo que AccountController.HashToken: así el enlace funciona en /Account/RestablecerPassword
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<ResultadoAcceso> EnviarInvitacionAsync(Usuario u, string baseUrl, string organizacion)
    {
        var destinos = NotificacionService.Destinos(u);
        if (destinos.Count == 0) return new ResultadoAcceso(false, false, "La persona no tiene un correo real registrado.");

        var ahora = DateTime.UtcNow;
        var recientes = await _context.TokensRecuperacion.CountAsync(t => t.UsuarioId == u.Id && t.FechaUtc > ahora.AddHours(-1));
        if (recientes >= 3) return new ResultadoAcceso(false, false, "Ya se enviaron 3 correos en la última hora. Espera un poco.");

        // Los enlaces anteriores quedan anulados
        var previos = await _context.TokensRecuperacion.Where(t => t.UsuarioId == u.Id && !t.Usado && t.ExpiraUtc > ahora).ToListAsync();
        foreach (var p in previos) p.Usado = true;

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _context.TokensRecuperacion.Add(new TokenRecuperacion
        {
            UsuarioId = u.Id,
            TokenHash = HashToken(token),
            FechaUtc = ahora,
            ExpiraUtc = ahora.AddHours(HorasInvitacion)
        });
        await _context.SaveChangesAsync();

        var raiz = UrlPublica.Obtener(null, _opt.UrlBase);
        if ((string.IsNullOrEmpty(raiz) || raiz.Contains("localhost")) && !string.IsNullOrWhiteSpace(baseUrl) && !baseUrl.Contains("localhost"))
            raiz = baseUrl.Replace("http://", baseUrl.Contains("ngrok") ? "https://" : "http://").TrimEnd('/');
        if (string.IsNullOrEmpty(raiz)) raiz = baseUrl.TrimEnd('/');
        var url = $"{raiz}/Account/RestablecerPassword?token={token}";
        var html = CorreoPlantilla.Construir(
            "Tu acceso a AUKA",
            $"Hola {u.Nombre}, {organizacion} te creó una cuenta en AUKA para tus citaciones con el equipo psicosocial.\n\n" +
            $"Tu usuario es: {u.Email}" + (string.IsNullOrWhiteSpace(u.Rut) ? "" : $" (también puedes entrar con tu RUT {u.Rut})") + ".\n\n" +
            $"Presiona el botón para crear tu contraseña. El enlace sirve una sola vez y vence en {HorasInvitacion} horas. " +
            "Si vence, usa «¿Olvidaste tu contraseña?» en la página de ingreso.",
            url, organizacion);

        var r = await _correo.EnviarAsync(destinos, "AUKA · Crea tu contraseña", html);
        return new ResultadoAcceso(r.Ok, r.Simulado, r.Ok ? null : r.Error);
    }
}
