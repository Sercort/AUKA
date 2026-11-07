namespace Auka.Web.Services;

/// <summary>RUT chileno: limpiar, validar el dígito verificador y dar formato 12.345.678-9.</summary>
public static class Rut
{
    public static string Normalizar(string? rut) =>
        new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static bool TryValidar(string? entrada, out string norm, out string formateado)
    {
        norm = formateado = "";
        var limpio = Normalizar(entrada);
        if (limpio.Length < 7 || limpio.Length > 9) return false;
        var cuerpo = limpio[..^1];
        var dv = limpio[^1];
        if (!cuerpo.All(char.IsDigit)) return false;

        var suma = 0; var factor = 2;
        foreach (var d in cuerpo.Reverse()) { suma += (d - '0') * factor; factor = factor == 7 ? 2 : factor + 1; }
        var resto = 11 - suma % 11;
        var esperado = resto == 11 ? '0' : resto == 10 ? 'K' : (char)('0' + resto);
        if (dv != esperado) return false;

        var partes = new List<string>();
        for (var i = cuerpo.Length; i > 0; i -= 3) partes.Insert(0, cuerpo.Substring(Math.Max(0, i - 3), Math.Min(3, i)));
        norm = limpio;
        formateado = string.Join(".", partes) + "-" + dv;
        return true;
    }
}
