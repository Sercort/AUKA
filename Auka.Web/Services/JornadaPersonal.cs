using System.Text.Json;
using Auka.Application.Entities;

namespace Auka.Web.Services;

/// <summary>
/// Jornada en el colegio del personal que no hace clases (director/a, UTP, administrador, inspector...).
/// La arma el administrador del colegio (también la suya). Se guarda en Usuario.JornadaPersonal como JSON.
/// Junto con el botón "Estoy en el colegio / No estoy", dice dónde está cada uno.
/// </summary>
public static class JornadaPersonal
{
    public sealed record Dia(int D, string Entrada, string Salida);
    public sealed class Datos
    {
        public List<Dia> Dias { get; set; } = new();
        public string? Nota { get; set; }
    }

    public static readonly string[] NombresDia = { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };

    public static Datos Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Datos();
        try { return JsonSerializer.Deserialize<Datos>(json) ?? new Datos(); } catch { return new Datos(); }
    }

    public static string? Escribir(Datos d) => d.Dias.Count == 0 && string.IsNullOrWhiteSpace(d.Nota) ? null : JsonSerializer.Serialize(d);

    /// <summary>Roles cuyo horario es una jornada (no clases). El administrador los arma.</summary>
    public static bool UsaJornada(RolUsuario r) => r is RolUsuario.SuperAdmin or RolUsuario.Director or RolUsuario.UTP or RolUsuario.Inspector
        or RolUsuario.Orientadora or RolUsuario.Psicologo or RolUsuario.TrabajadorSocial or RolUsuario.Psicopedagogo;

    /// <summary>
    /// Dónde está ahora: lo que marcó hoy con el botón manda; si no marcó nada, se calcula con su jornada.
    /// Devuelve (texto, color: "verde" | "gris" | "teal").
    /// </summary>
    public static (string Texto, string Chip) Estado(Usuario u)
    {
        var ahora = DateTime.Now;
        if (u.EnColegio != null && u.EnColegioUtc != null && u.EnColegioUtc.Value.ToLocalTime().Date == ahora.Date)
            return u.EnColegio.Value ? ("🟢 En el colegio", "verde") : ("⚪ No está en el colegio hoy", "");
        var j = Leer(u.JornadaPersonal);
        var hoy = j.Dias.FirstOrDefault(x => x.D == (int)ahora.DayOfWeek);
        if (hoy == null) return j.Dias.Count == 0 ? ("Sin jornada registrada", "") : ("Hoy no le toca venir", "");
        if (TimeSpan.TryParse(hoy.Entrada, out var e) && TimeSpan.TryParse(hoy.Salida, out var s))
            return ahora.TimeOfDay >= e && ahora.TimeOfDay < s ? ($"🕘 En su jornada ({hoy.Entrada}–{hoy.Salida})", "teal") : ($"Fuera de su jornada (hoy {hoy.Entrada}–{hoy.Salida})", "");
        return ("Sin jornada registrada", "");
    }
}
