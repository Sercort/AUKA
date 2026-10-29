namespace Auka.Application.Interfaces;

public interface ICorreoService
{
    /// <summary>Envía un correo HTML. Simulado = true cuando el envío real está apagado (modo pruebas).</summary>
    Task<(bool Ok, bool Simulado, string? Error)> EnviarAsync(
        IEnumerable<string> destinos, string asunto, string cuerpoHtml, CancellationToken ct = default);
}
