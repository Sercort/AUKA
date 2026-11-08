using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace Auka.Web.Services;

/// <summary>
/// Gráficos livianos para los paneles (sin librerías: SVG y CSS). Los usan el panel UTP y el inicio del profesor/a.
/// Dona = porcentaje · Linea = tendencia en el tiempo · Spark = mini tendencia · Columnas = cantidades por grupo.
/// </summary>
public static class Graficos
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string F(double d) => d.ToString("0.#", Inv);

    /// <summary>Dona de porcentaje con el texto al centro.</summary>
    public static IHtmlContent Dona(decimal? pct, string color, string? centro = null, int tam = 64)
    {
        var p = (double)Math.Clamp(pct ?? 0, 0, 100);
        var txt = centro ?? (pct == null ? "—" : pct.Value.ToString("0.#", new CultureInfo("es-CL")) + "%");
        return new HtmlString($"<span class=\"gx-dona\" style=\"--p:{F(p)};--c:{color};--t:{tam}px\"><b>{E(txt)}</b></span>");
    }

    /// <summary>Línea de tendencia con área, puntos (con su valor al pasar el mouse) y una línea de meta opcional.</summary>
    public static IHtmlContent Linea(IReadOnlyList<decimal?> vals, IReadOnlyList<string> etq, decimal min, decimal max, string color, decimal? meta = null, string? sufijo = null)
    {
        if (vals.Count == 0 || vals.All(v => v == null)) return new HtmlString("<p class=\"text-muted small mb-0\">Aún no hay datos para graficar.</p>");
        const double W = 340, H = 130, pl = 30, pr = 10, pt = 12, pb = 24;
        double X(int i) => vals.Count == 1 ? pl + (W - pl - pr) / 2 : pl + i * (W - pl - pr) / (vals.Count - 1);
        double Y(decimal v) => pt + (double)((max - Math.Clamp(v, min, max)) / (max - min)) * (H - pt - pb);
        var id = "g" + Guid.NewGuid().ToString("N")[..6];
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"gx-linea\" viewBox=\"0 0 {F(W)} {F(H)}\" role=\"img\"><defs><linearGradient id=\"{id}\" x1=\"0\" x2=\"0\" y1=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"{color}\" stop-opacity=\".28\"/><stop offset=\"1\" stop-color=\"{color}\" stop-opacity=\"0\"/></linearGradient></defs>");
        // Grilla: 3 líneas suaves con su valor
        for (var k = 0; k <= 2; k++)
        {
            var v = min + (max - min) * k / 2;
            var y = Y(v);
            sb.Append($"<line x1=\"{F(pl)}\" x2=\"{F(W - pr)}\" y1=\"{F(y)}\" y2=\"{F(y)}\" class=\"gx-grid\"/><text x=\"{F(pl - 5)}\" y=\"{F(y + 3)}\" class=\"gx-ey\">{E(v.ToString("0.#", Inv))}</text>");
        }
        if (meta != null)
        {
            var ym = Y(meta.Value);
            sb.Append($"<line x1=\"{F(pl)}\" x2=\"{F(W - pr)}\" y1=\"{F(ym)}\" y2=\"{F(ym)}\" class=\"gx-meta\"/>");
        }
        var pts = vals.Select((v, i) => (v, i)).Where(t => t.v != null).Select(t => (X: X(t.i), Y: Y(t.v!.Value), V: t.v!.Value, I: t.i)).ToList();
        var linea = string.Join(" ", pts.Select(p => $"{F(p.X)},{F(p.Y)}"));
        if (pts.Count > 1)
            sb.Append($"<polygon points=\"{F(pts[0].X)},{F(H - pb)} {linea} {F(pts[^1].X)},{F(H - pb)}\" fill=\"url(#{id})\"/>");
        sb.Append($"<polyline points=\"{linea}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.6\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        foreach (var p in pts)
            sb.Append($"<circle cx=\"{F(p.X)}\" cy=\"{F(p.Y)}\" r=\"3.6\" fill=\"#fff\" stroke=\"{color}\" stroke-width=\"2\"><title>{E(etq.ElementAtOrDefault(p.I))}: {E(p.V.ToString("0.#", new CultureInfo("es-CL")) + sufijo)}</title></circle>");
        // Etiquetas abajo: no más de 7 para que no se monten
        var paso = Math.Max(1, (int)Math.Ceiling(vals.Count / 7.0));
        for (var i = 0; i < vals.Count; i += paso)
            sb.Append($"<text x=\"{F(X(i))}\" y=\"{F(H - 6)}\" class=\"gx-ex\">{E(etq.ElementAtOrDefault(i))}</text>");
        sb.Append("</svg>");
        return new HtmlString(sb.ToString());
    }

    /// <summary>Mini tendencia para el encabezado de una tarjeta.</summary>
    public static IHtmlContent Spark(IReadOnlyList<decimal?> vals, string color)
    {
        var v = vals.Where(x => x != null).Select(x => (double)x!.Value).ToList();
        if (v.Count < 2) return HtmlString.Empty;
        double mn = v.Min(), mx = v.Max(); if (mx - mn < 0.001) { mn -= 1; mx += 1; }
        var pts = string.Join(" ", v.Select((x, i) => $"{F(i * 70.0 / (v.Count - 1) + 2)},{F(22 - (x - mn) / (mx - mn) * 18)}"));
        return new HtmlString($"<svg class=\"gx-spark\" viewBox=\"0 0 74 26\"><polyline points=\"{pts}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/></svg>");
    }

    /// <summary>Columnas verticales (cantidad por grupo), con el número arriba.</summary>
    public static IHtmlContent Columnas(IReadOnlyList<(string Etq, int N, string Color)> datos, int alto = 120)
    {
        if (datos.Count == 0) return new HtmlString("<p class=\"text-muted small mb-0\">Aún no hay datos para graficar.</p>");
        var max = Math.Max(1, datos.Max(d => d.N));
        var sb = new StringBuilder($"<div class=\"gx-cols\" style=\"--h:{alto}px\">");
        foreach (var d in datos)
        {
            var h = d.N == 0 ? 2 : Math.Max(6, d.N * 100 / max);
            sb.Append($"<div class=\"c\" title=\"{E(d.Etq)}: {d.N}\"><b>{d.N}</b><span style=\"height:{h}%;background:{d.Color}\"></span><small>{E(d.Etq)}</small></div>");
        }
        sb.Append("</div>");
        return new HtmlString(sb.ToString());
    }

    /// <summary>Color según la nota (rojo &lt; 4, ámbar &lt; 5, verde).</summary>
    public static string ColorNota(decimal? n) => n == null ? "#8a97ab" : n < 4.0m ? "#d64545" : n < 5.0m ? "#e09a00" : "#16915c";
    /// <summary>Color según el % de asistencia (rojo bajo 85%).</summary>
    public static string ColorAsistencia(decimal? p) => p == null ? "#8a97ab" : p < 85m ? "#d64545" : p < 90m ? "#e09a00" : "#16915c";

    /// <summary>Notas en 6 tramos (1,0–1,9 ... 6,0–7,0) para el gráfico de distribución.</summary>
    public static List<(string Etq, int N, string Color)> Distribucion(IEnumerable<decimal> notas)
    {
        var t = new int[6];
        foreach (var n in notas) t[Math.Clamp((int)Math.Floor(n) - 1, 0, 5)]++;
        var col = new[] { "#d64545", "#e2614d", "#ef8a5a", "#e0b100", "#4bbf8a", "#16915c" };
        return Enumerable.Range(0, 6).Select(i => ($"{i + 1},0–{(i == 5 ? "7,0" : (i + 1) + ",9")}", t[i], col[i])).ToList();
    }
}
