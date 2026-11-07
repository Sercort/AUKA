namespace Auka.Web.Services;

/// <summary>Niveles escolares chilenos: orden natural y ciclo (para agrupar los cursos como carpetas).</summary>
public static class Niveles
{
    public static readonly string[] Oficiales =
    {
        "Prekínder", "Kínder",
        "1º Básico", "2º Básico", "3º Básico", "4º Básico", "5º Básico", "6º Básico", "7º Básico", "8º Básico",
        "1º Medio", "2º Medio", "3º Medio", "4º Medio"
    };

    // "5° básico", "5º Básico" y "III° Medio" se reconocen como el nivel oficial
    public static string Normal(string? n)
    {
        var x = (n ?? "").Trim().ToLowerInvariant().Replace("°", "º").Replace("í", "i").Replace("á", "a").Replace("é", "e");
        var rom = System.Text.RegularExpressions.Regex.Match(x, @"^(iv|iii|ii|i)\s*º?\s+medio");
        if (rom.Success) x = (rom.Groups[1].Value switch { "i" => "1", "ii" => "2", "iii" => "3", _ => "4" }) + "º medio" + x[rom.Length..];
        x = System.Text.RegularExpressions.Regex.Replace(x, @"^(\d)\s*º?\s*", "$1º ");
        x = x.Replace("pre-kinder", "prekinder").Replace("pre kinder", "prekinder");
        return System.Text.RegularExpressions.Regex.Replace(x, @"\s+", " ");
    }

    public static int Orden(string? n)
    {
        var i = Array.FindIndex(Oficiales, o => Normal(o) == Normal(n));
        return i < 0 ? 99 : i;
    }

    public static string Ciclo(string? n)
    {
        var x = Normal(n);
        if (x.Contains("kinder") || x.StartsWith("nt")) return "Educación parvularia";
        if (x.Contains("medio")) return "Educación media";
        if (x.Contains("basico")) return "Educación básica";
        return "Otros cursos";
    }

    public static int OrdenCiclo(string ciclo) => ciclo switch
    {
        "Educación parvularia" => 0, "Educación básica" => 1, "Educación media" => 2, _ => 3
    };
}
