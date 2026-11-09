using System.Text.Json;

namespace Auka.Web.Services;

/// <summary>Metas del año que fija la dirección (visión estratégica): se guardan en Colegio.MetasDireccion.</summary>
public sealed class MetasDireccion
{
    public decimal? Asistencia { get; set; } = 90;     // % de asistencia del colegio
    public decimal? Promedio { get; set; } = 5.5m;     // promedio general
    public int? Matricula { get; set; }                // alumnos/as matriculados
    public int? NegativasMes { get; set; }             // máximo de anotaciones negativas en el mes

    public static MetasDireccion Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MetasDireccion();
        try { return JsonSerializer.Deserialize<MetasDireccion>(json) ?? new MetasDireccion(); } catch { return new MetasDireccion(); }
    }

    public string Escribir() => JsonSerializer.Serialize(this);
}
