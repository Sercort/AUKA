namespace Auka.Web.Helpers;

public static class RutUtils
{
    /// <summary>
    /// Limpia el RUT dejando solo números y 'K' (Ej: "12.345.678-9" -> "123456789")
    /// </summary>
    public static string LimpiarRut(string rut)
    {
        if (string.IsNullOrWhiteSpace(rut)) return string.Empty;
        return rut.Replace(".", "").Replace("-", "").Trim().ToUpper();
    }

    /// <summary>
    /// Valida si un RUT es matemáticamente correcto (Módulo 11)
    /// </summary>
    public static bool ValidarRut(string rut)
    {
        rut = LimpiarRut(rut);
        if (rut.Length < 8) return false;

        string rutNumeros = rut.Substring(0, rut.Length - 1);
        char dvIngresado = rut[rut.Length - 1];

        if (!int.TryParse(rutNumeros, out int numero)) return false;

        int suma = 0;
        int multiplicador = 2;

        while (numero > 0)
        {
            int digito = numero % 10;
            suma += digito * multiplicador;
            numero /= 10;
            multiplicador = (multiplicador == 7) ? 2 : multiplicador + 1;
        }

        int resto = suma % 11;
        int dvCalculado = 11 - resto;

        char dvEsperado = dvCalculado switch
        {
            11 => '0',
            10 => 'K',
            _ => dvCalculado.ToString()[0]
        };

        return dvIngresado == dvEsperado;
    }
}