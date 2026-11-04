using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Auka.Web.Services;

/// <summary>
/// Palabras que cambian según el tipo de organización.
/// Colegio -> alumno/a y curso. Empresa, instituto, fundación o particular -> paciente, sin curso.
/// (Sin tildes a propósito: Razor las codifica como entidades HTML dentro de los scripts.)
/// </summary>
public sealed record Vocabulario(string Tipo, bool EsColegio, string persona, string personas, string Persona, string Personas)
{
    public static readonly Vocabulario Colegio = new("Colegio", true, "alumno/a", "alumnos/as", "Alumno/a", "Alumnos/as");

    public static Vocabulario Para(string? tipo) =>
        string.IsNullOrWhiteSpace(tipo) || tipo == "Colegio"
            ? Colegio
            : new(tipo, false, "paciente", "pacientes", "Paciente", "Pacientes");
}

/// <summary>Tipos de organización y planillas que el dueño de la plataforma puede activar en cada una.</summary>
public static class Organizacion
{
    public static readonly string[] Tipos = { "Colegio", "Empresa", "Instituto", "Fundación", "Particular" };

    // Clave de la planilla (pestaña) -> nombre visible
    public static readonly Dictionary<string, string> Planillas = new()
    {
        ["seg"] = "Seguimiento",
        ["jud"] = "Judicial",
        ["der"] = "Derivaciones",
        ["vd"] = "Terreno",
        ["ben"] = "Beneficios",
        ["vac"] = "Vacunación",
        ["egr"] = "Egresos y plan"
    };

    // Planillas que se activan por defecto al crear una organización que no es colegio
    public const string PlanillasPorDefectoOrganizacion = "seg,der,vd,egr";

    // Qué planilla necesita cada ficha de "+ Nuevo registro"
    public static readonly Dictionary<string, string> PlanillaDeFicha = new()
    {
        ["seg"] = "seg", ["jud"] = "jud", ["inf"] = "jud", ["vd"] = "vd", ["conv"] = "der", ["tut"] = "der",
        ["plan"] = "egr", ["egr"] = "egr", ["pro"] = "ben", ["ropa"] = "ben"
    };

    /// <summary>null o vacío = todas las planillas.</summary>
    public static string[] Activas(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Planillas.Keys.ToArray()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(Planillas.ContainsKey).Distinct().ToArray();

    /// <summary>Limpia lo que llega del formulario: solo claves válidas; todas marcadas = null.</summary>
    public static string? Normalizar(string? csv)
    {
        var l = Activas(csv);
        return l.Length == Planillas.Count || string.IsNullOrWhiteSpace(csv) ? null : string.Join(",", l);
    }

    public static string LlaveCache(int colegioId) => $"org:{colegioId}";
}

/// <summary>Datos de la organización de la sesión (se guardan 5 minutos en memoria).</summary>
public sealed record DatosOrganizacion(Vocabulario V, string[] Planillas);

/// <summary>
/// Filtro global: deja en ViewData["Org"] el vocabulario y las planillas activas de la organización
/// de la sesión, para que cualquier vista o layout diga "alumno/a" o "paciente" según corresponda.
/// </summary>
public class OrganizacionFilter : IAsyncActionFilter
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public OrganizacionFilter(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (ctx.Controller is Controller c)
            c.ViewData["Org"] = await ObtenerAsync(_context, _cache);
        await next();
    }

    public static async Task<DatosOrganizacion> ObtenerAsync(ApplicationDbContext context, IMemoryCache cache)
    {
        var id = context.CurrentTenantId;
        if (id <= 0) return new DatosOrganizacion(Vocabulario.Colegio, Organizacion.Planillas.Keys.ToArray());

        return (await cache.GetOrCreateAsync(Organizacion.LlaveCache(id), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var col = await context.Colegios.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.TipoOrganizacion, x.PlanillasActivas })
                .FirstOrDefaultAsync();
            return new DatosOrganizacion(Vocabulario.Para(col?.TipoOrganizacion), Organizacion.Activas(col?.PlanillasActivas));
        }))!;
    }
}
