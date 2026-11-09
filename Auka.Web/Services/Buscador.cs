namespace Auka.Web.Services;

/// <summary>
/// Búsqueda "a prueba de errores" para todos los buscadores de AUKA:
/// no importan mayúsculas ni tildes, sirve con nombre y apellido juntos ("paola campos"), con una sola palabra
/// ("paola" muestra a todas las Paola; "herrera" a todos los Herrera), con el principio de la palabra ("camp")
/// y con letras de más, de menos o cambiadas ("campo", "canpos", "paloa"). Primero salen los que calzan mejor.
/// </summary>
public static class Buscador
{
    /// <summary>Palabras de la búsqueda, sin tildes ni mayúsculas.</summary>
    public static string[] Palabras(string? q) =>
        Csv.Clave(q).Replace("º", " ").Replace("°", " ").Replace(".", "").Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 0).ToArray();

    /// <summary>
    /// Puntaje de qué tan bien calza el texto (nombre completo) con la búsqueda. 0 = no calza.
    /// Basta con que calce UNA palabra; si calzan todas, sube mucho el puntaje.
    /// </summary>
    public static double Puntaje(string[] busqueda, string? texto)
    {
        if (busqueda.Length == 0) return 0;
        var partes = Palabras(texto);
        if (partes.Length == 0) return 0;
        double total = 0; var calzan = 0;
        foreach (var b in busqueda)
        {
            double mejor = 0;
            foreach (var p in partes) mejor = Math.Max(mejor, Palabra(b, p));
            if (mejor > 0) { calzan++; total += mejor; }
        }
        if (calzan == 0) return 0;
        // Todas las palabras calzan (nombre + apellido): arriba de todo
        if (calzan == busqueda.Length && busqueda.Length > 1) total += 5;
        // Frase seguida ("paola campos" dentro de "paola andrea campos")
        if (busqueda.Length > 1 && string.Join(' ', partes).Contains(string.Join(' ', busqueda))) total += 2;
        return total;
    }

    /// <summary>Qué tan bien calza una palabra buscada con una palabra del nombre.</summary>
    private static double Palabra(string b, string p)
    {
        if (b == p) return 4;
        if (p.StartsWith(b)) return b.Length >= 2 ? 3 : 1;     // va escribiendo: "pao", "camp"
        if (b.Length >= 3 && p.Contains(b)) return 2;
        if (b.Length < 3) return 0;
        // Errores de tipeo: compara con la palabra completa y con su principio del mismo largo
        var tolera = b.Length <= 4 ? 1 : b.Length <= 7 ? 2 : 3;
        var d = Math.Min(Distancia(b, p), p.Length > b.Length ? Distancia(b, p[..b.Length]) : int.MaxValue);
        if (d <= tolera) return 2.5 - d * 0.6;
        // Suena igual (b/v, s/z/c, y/ll, h muda): "herera" ≈ "herrera", "baldes" ≈ "valdes"
        if (Sonido(b) == Sonido(p) || (p.Length > b.Length && Sonido(p).StartsWith(Sonido(b)))) return 1.5;
        return 0;
    }

    /// <summary>Distancia de edición (letras de más, de menos, cambiadas o dadas vuelta).</summary>
    public static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var c = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + c);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>Cómo suena en español (para quien no sabe cómo se escribe).</summary>
    private static string Sonido(string s)
    {
        s = s.Replace("ll", "y").Replace("qu", "k").Replace("ch", "x").Replace("ce", "se").Replace("ci", "si")
             .Replace('v', 'b').Replace('z', 's').Replace('c', 'k').Replace("h", "").Replace('ñ', 'n').Replace('w', 'u');
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s) if (sb.Length == 0 || sb[^1] != ch) sb.Append(ch);   // "rr" = "r"
        return sb.ToString();
    }

    /// <summary>¿La búsqueda parece un RUT? (tiene números)</summary>
    public static bool EsRut(string? q) => (q ?? "").Count(char.IsDigit) >= 3;

    /// <summary>RUT sin puntos, guion ni espacios, en mayúscula.</summary>
    public static string RutPlano(string? r) => new string((r ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
