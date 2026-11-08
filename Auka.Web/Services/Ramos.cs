namespace Auka.Web.Services;

/// <summary>Ícono y color de cada asignatura (los mismos en el inicio del profesor/a, su horario y sus listas).</summary>
public static class Ramos
{
    public static (string Icono, string Color, string Fondo) Estilo(string? ramo)
    {
        var k = Csv.Clave(ramo);
        if (k.Contains("lenguaje") || k.Contains("lengua y lit")) return ("📖", "#2459b0", "#e8efff");
        if (k.Contains("matematica")) return ("➗", "#6c3fc4", "#f0eaff");
        if (k.Contains("historia")) return ("🌎", "#a85a00", "#fff1dd");
        if (k.Contains("ciencias para")) return ("🌱", "#12744a", "#e2f6ec");
        if (k.Contains("ciencias") || k.Contains("biologia")) return ("🔬", "#12744a", "#e2f6ec");
        if (k.Contains("quimica")) return ("⚗️", "#0c6f7a", "#e0f6f8");
        if (k.Contains("fisica") && !k.Contains("educacion")) return ("🧲", "#0c6f7a", "#e0f6f8");
        if (k.Contains("ingles")) return ("🇬🇧", "#b03a37", "#fde8e7");
        if (k.Contains("educacion fisica")) return ("⚽", "#2a8a3e", "#e4f7e8");
        if (k.Contains("artes")) return ("🎨", "#c2410c", "#ffeadd");
        if (k.Contains("musica")) return ("🎵", "#be185d", "#fde7f1");
        if (k.Contains("tecnologia")) return ("💻", "#334155", "#e9eef5");
        if (k.Contains("religion")) return ("🕊️", "#6d5b2e", "#f6f0e1");
        if (k.Contains("filosofia")) return ("🦉", "#5b4b8a", "#efebfb");
        if (k.Contains("ciudadan")) return ("🏛️", "#1e40af", "#e6ecfb");
        if (k.Contains("especialidad")) return ("🛠️", "#9a3412", "#fdeee4");
        if (k.Contains("orientacion") || k.Contains("consejo")) return ("🧭", "#0f766e", "#e0f5f2");
        return ("📘", "#2459b0", "#e8efff");
    }
}
