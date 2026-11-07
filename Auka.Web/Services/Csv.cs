using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Auka.Web.Services;

/// <summary>
/// Lectura y escritura de planillas CSV (Excel › Guardar como › CSV delimitado por punto y coma).
/// Se usa en las importaciones/exportaciones de notas y anotaciones.
/// </summary>
public static class Csv
{
    /// <summary>Quita tildes y pasa a minúsculas: "Teléfono" → "telefono". Sirve para comparar encabezados y nombres.</summary>
    public static string Clave(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var n = s.Trim().TrimStart('﻿').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in n)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Planilla leída: encabezados normalizados y filas (sin líneas vacías).</summary>
    public sealed class Planilla
    {
        public List<string> Cabecera { get; init; } = new();
        public List<string[]> Filas { get; init; } = new();

        /// <summary>Índice de la primera columna cuyo encabezado empieza con alguna de las claves (sin tildes).</summary>
        public int Col(params string[] claves) => Cabecera.FindIndex(h => claves.Any(k => h.StartsWith(k)));

        public static string Celda(string[] f, int i) => i >= 0 && i < f.Length ? f[i].Trim().Trim('"').Trim() : "";
    }

    /// <summary>Lee el archivo subido. Detecta solo si el separador es ';' o ','. Acepta UTF-8 (con o sin BOM) y el formato de Excel en Windows.</summary>
    public static async Task<Planilla> LeerAsync(IFormFile archivo)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await archivo.CopyToAsync(ms);
            bytes = ms.ToArray();
        }
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            // Excel en Windows a veces guarda en Latin-1 (Windows-1252): así no se pierden las tildes ni la ñ
            texto = Encoding.Latin1.GetString(bytes);
        }
        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l) && l.Trim().Trim(';', ',').Length > 0).ToList();
        if (lineas.Count == 0) return new Planilla();

        var sep = lineas[0].Count(c => c == ';') >= lineas[0].Count(c => c == ',') ? ';' : ',';
        return new Planilla
        {
            Cabecera = lineas[0].Split(sep).Select(h => Clave(h.Trim().Trim('"'))).ToList(),
            Filas = lineas.Skip(1).Select(l => l.Split(sep)).ToList()
        };
    }

    /// <summary>Arma un CSV que Excel abre directo con tildes (UTF-8 con BOM y punto y coma).</summary>
    public static byte[] Escribir(IEnumerable<IEnumerable<string?>> filas)
    {
        static string Esc(string? v)
        {
            v ??= "";
            return v.IndexOfAny(new[] { ';', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
        var sb = new StringBuilder();
        foreach (var f in filas) sb.Append(string.Join(';', f.Select(Esc))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>Lee una nota chilena: "5,5", "5.5" o "55" (= 5,5). Devuelve null si no es válida (1,0 a 7,0).</summary>
    public static decimal? Nota(string? txt)
    {
        if (string.IsNullOrWhiteSpace(txt)) return null;
        txt = txt.Trim().Replace(',', '.');
        if (!decimal.TryParse(txt, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return null;
        if (n >= 10 && n <= 70 && !txt.Contains('.')) n /= 10m;   // "55" se entiende como 5,5
        n = Math.Round(n, 1, MidpointRounding.AwayFromZero);
        return n >= 1.0m && n <= 7.0m ? n : null;
    }

    /// <summary>Fecha en formatos comunes (dd-mm-aaaa, dd/mm/aaaa, aaaa-mm-dd). Devuelve UTC (lo pide PostgreSQL).</summary>
    public static DateTime? Fecha(string? txt)
    {
        if (string.IsNullOrWhiteSpace(txt)) return null;
        var formatos = new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yy", "dd/MM/yy" };
        if (DateTime.TryParseExact(txt.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return DateTime.SpecifyKind(d.Date.AddHours(12), DateTimeKind.Utc);
        return null;
    }

    public static string Nota1(decimal n) => n.ToString("0.0", CultureInfo.GetCultureInfo("es-CL"));
}
