using System.Security.Claims;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// Permisos que el administrador del colegio entrega a otros roles (Plan Premium).
/// El administrador (SuperAdmin) siempre puede todo; el resto, solo lo que el administrador marcó.
/// Se guardan en Colegio.Permisos como "clubes:Director,UTP;inscribir:UTP,InspectorGeneral;horarios:UTP;profjefe:UTP".
/// </summary>
public static class Permisos
{
    public const string Clubes = "clubes";        // crear clubes y talleres
    public const string Inscribir = "inscribir";  // inscribir y quitar alumnos de un club
    public const string Horarios = "horarios";    // armar horarios de cursos y profesores
    public const string ProfJefe = "profjefe";    // asignar profesores jefes

    public record Accion(string Clave, string Titulo, string Ayuda, string[] Roles, string[] PorDefecto);

    /// <summary>Qué puede entregar el administrador y a quién. El orden es el que se muestra en el panel.</summary>
    public static readonly Accion[] Acciones =
    {
        new(Clubes, "Crear clubes y talleres", "Crean el club y eligen al profesor a cargo.",
            new[] { "Director", "UTP", "Orientadora" }, new[] { "Director", "UTP" }),
        new(Inscribir, "Inscribir alumnos en clubes", "Suman o quitan integrantes de un club.",
            new[] { "Director", "UTP", "Orientadora", "InspectorGeneral", "ProfesorCargo" }, new[] { "UTP", "InspectorGeneral", "ProfesorCargo" }),
        new(Horarios, "Armar horarios", "Horarios de cada curso y de cada profesor. Los demás solo los ven.",
            new[] { "UTP", "Orientadora" }, new[] { "UTP" }),
        new(ProfJefe, "Asignar profesores jefes", "Elige el profesor jefe de cada curso.",
            new[] { "UTP", "Director" }, new[] { "UTP" }),
    };

    public static string NombreRol(string r) => r switch
    {
        "Director" => "Director/a",
        "UTP" => "Jefe/a de UTP",
        "Orientadora" => "Orientador/a",
        "InspectorGeneral" => "Inspector/a general",
        "ProfesorCargo" => "Profesor/a a cargo del club",
        _ => r
    };

    public static bool EsAdministrador(ClaimsPrincipal u) => u.IsInRole("SuperAdmin");

    // Se mantiene por compatibilidad: administración del colegio (para ver paneles)
    public static bool EsAdministracion(ClaimsPrincipal u) =>
        u.IsInRole("SuperAdmin") || u.IsInRole("Director") || u.IsInRole("Administrador");

    /// <summary>Lee los permisos guardados. Si nunca se guardaron, usa los de por defecto
    /// (y para clubes respeta lo que se eligió antes en "¿Quién puede crear clubes?").</summary>
    public static Dictionary<string, HashSet<string>> Leer(string? texto, string? clubesAntiguo = null)
    {
        var d = Acciones.ToDictionary(a => a.Clave, a => new HashSet<string>(a.PorDefecto));
        if (texto == null)
        {
            if (clubesAntiguo != null)   // el director antes siempre podía: se mantiene
                d[Clubes] = new HashSet<string>(clubesAntiguo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append("Director"));
            return d;
        }
        foreach (var parte in texto.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = parte.Split(':', 2);
            var acc = Acciones.FirstOrDefault(a => a.Clave == kv[0].Trim());
            if (acc == null) continue;
            d[acc.Clave] = new HashSet<string>((kv.Length > 1 ? kv[1] : "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(r => acc.Roles.Contains(r)));
        }
        return d;
    }

    public static string Escribir(Dictionary<string, HashSet<string>> d) =>
        string.Join(";", Acciones.Select(a => a.Clave + ":" + string.Join(",", d.TryGetValue(a.Clave, out var r) ? r.Where(a.Roles.Contains) : Enumerable.Empty<string>())));

    public static async Task<Dictionary<string, HashSet<string>>> CargarAsync(ApplicationDbContext db, ClaimsPrincipal u)
    {
        _ = int.TryParse(u.FindFirst("ColegioId")?.Value, out var colegioId);
        var c = await db.Colegios.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == colegioId).Select(x => new { x.Permisos, x.ClubesPermitidos }).FirstOrDefaultAsync();
        return Leer(c?.Permisos, c?.ClubesPermitidos);
    }

    /// <summary>¿El usuario puede hacer esta acción? docenteACargoId: el profesor a cargo del club (para "inscribir").</summary>
    public static async Task<bool> PuedeAsync(ApplicationDbContext db, ClaimsPrincipal u, string accion, int? docenteACargoId = null)
    {
        if (EsAdministrador(u)) return true;
        var permitidos = (await CargarAsync(db, u)).GetValueOrDefault(accion) ?? new HashSet<string>();
        if (permitidos.Contains("Director") && u.IsInRole("Director")) return true;
        if (permitidos.Contains("UTP") && u.IsInRole("UTP")) return true;
        if (permitidos.Contains("Orientadora") && u.IsInRole("Orientadora")) return true;

        _ = int.TryParse(u.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId);
        if (permitidos.Contains("ProfesorCargo") && docenteACargoId != null && docenteACargoId == userId) return true;
        if (permitidos.Contains("InspectorGeneral"))
            return await db.Usuarios.AsNoTracking().AnyAsync(x => x.Id == userId && x.Activo && x.EsInspectorGeneral);
        return false;
    }

    public static Task<bool> PuedeCrearClubesAsync(ApplicationDbContext db, ClaimsPrincipal u) => PuedeAsync(db, u, Clubes);
}
