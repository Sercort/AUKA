namespace Auka.Web.Services;

/// <summary>Saludo según la hora en que entra: buenos días (hasta 12:00), buenas tardes (hasta 20:00), buenas noches.</summary>
public static class Saludo
{
    public static string Ahora()
    {
        var h = DateTime.Now.Hour;
        return h < 12 ? "Buenos días" : h < 20 ? "Buenas tardes" : "Buenas noches";
    }
}
