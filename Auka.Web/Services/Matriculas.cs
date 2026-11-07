using System.Security.Claims;
using System.Text.Json;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Services;

/// <summary>
/// Períodos de matrícula del colegio. La UTP, el director/a o el administrador abren "Matrícula 2027" con fechas
/// y eligen quiénes pueden matricular (UTP, profesores jefe, todos los profesores, orientador/a...).
/// Mientras está abierta, a esas personas les aparece el aviso y el botón "Matricular"; al cerrarla (o al pasar la fecha) desaparece.
/// Sin períodos creados funciona como siempre: la administración matricula el año actual y, desde septiembre, el siguiente.
/// Se guarda como JSON en Colegio.PeriodosMatricula.
/// </summary>
public static class Matriculas
{
    public sealed class Periodo
    {
        public int Anio { get; set; }
        public DateOnly Desde { get; set; }
        public DateOnly? Hasta { get; set; }
        public List<string> Roles { get; set; } = new();
        public bool Cerrado { get; set; }
        public string? CambioPor { get; set; }
        public DateTime CambioUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Quiénes pueden recibir permiso para matricular (el administrador siempre puede).</summary>
    public static readonly (string Clave, string Nombre, string Ayuda)[] Roles =
    {
        ("UTP", "Jefe/a de UTP", ""),
        ("Director", "Director/a", ""),
        ("ProfesorJefe", "Profesores/as jefe", "Cada profesor/a jefe matricula (o renueva) a los alumnos/as."),
        ("Docente", "Todos los profesores/as", "Cualquier profesor/a del colegio."),
        ("Orientadora", "Orientador/a", ""),
        ("InspectorGeneral", "Inspector/a general", ""),
    };

    public static string NombreRol(string clave) => Roles.FirstOrDefault(r => r.Clave == clave).Nombre ?? clave;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static List<Periodo> Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<Periodo>>(json, Json) ?? new(); }
        catch { return new(); }
    }

    public static string? Escribir(List<Periodo> lista) =>
        lista.Count == 0 ? null : JsonSerializer.Serialize(lista.OrderBy(p => p.Anio).ToList(), Json);

    public static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);

    public static bool Abierto(Periodo p) => !p.Cerrado && Hoy >= p.Desde && (p.Hasta == null || Hoy <= p.Hasta);

    /// <summary>"abierta", "programada" (aún no empieza), "terminada" (pasó la fecha) o "cerrada" (la cerraron a mano).</summary>
    public static string Estado(Periodo p) =>
        p.Cerrado ? "cerrada" : Hoy < p.Desde ? "programada" : p.Hasta != null && Hoy > p.Hasta ? "terminada" : "abierta";

    /// <summary>Quienes administran los períodos (abrir, cerrar, elegir quién matricula).</summary>
    public static bool EsGestor(ClaimsPrincipal u) => u.IsInRole("SuperAdmin") || u.IsInRole("UTP") || u.IsInRole("Director");

    public static async Task<List<Periodo>> CargarAsync(ApplicationDbContext db, ClaimsPrincipal u)
    {
        _ = int.TryParse(u.FindFirst("ColegioId")?.Value, out var colegioId);
        var json = await db.Colegios.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == colegioId).Select(c => c.PeriodosMatricula).FirstOrDefaultAsync();
        return Leer(json);
    }

    /// <summary>¿Esta persona está entre los elegidos del período?</summary>
    public static async Task<bool> CalzaAsync(ApplicationDbContext db, ClaimsPrincipal u, Periodo p)
    {
        if (u.IsInRole("SuperAdmin")) return true;
        if (p.Roles.Contains("UTP") && u.IsInRole("UTP")) return true;
        if (p.Roles.Contains("Director") && u.IsInRole("Director")) return true;
        if (p.Roles.Contains("Orientadora") && u.IsInRole("Orientadora")) return true;
        if (p.Roles.Contains("Docente") && u.IsInRole("Docente")) return true;
        _ = int.TryParse(u.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var yo);
        if (p.Roles.Contains("ProfesorJefe") && u.IsInRole("Docente"))
        {
            var anio = DateTime.Now.Year;
            if (await db.Cursos.AsNoTracking().AnyAsync(c => c.ProfesorJefeId == yo && (c.AnioAcademico == anio || c.AnioAcademico == p.Anio))) return true;
        }
        if (p.Roles.Contains("InspectorGeneral") && u.IsInRole("Inspector"))
            return await db.Usuarios.AsNoTracking().AnyAsync(x => x.Id == yo && x.Activo && x.EsInspectorGeneral);
        return false;
    }

    /// <summary>Años en que esta persona puede matricular hoy (ordenados). Vacío = no puede matricular.</summary>
    public static async Task<List<int>> AniosQuePuedeAsync(ApplicationDbContext db, ClaimsPrincipal u)
    {
        var periodos = await CargarAsync(db, u);
        var actual = DateTime.Now.Year;
        var anios = new HashSet<int>();

        // La administración siempre matricula el año en curso (alumnos/as que llegan durante el año)
        if (EsGestor(u)) anios.Add(actual);
        // Automático: sin período creado para el año siguiente, la administración puede matricularlo desde septiembre
        if (EsGestor(u) && DateTime.Now.Month >= 9 && !periodos.Any(p => p.Anio == actual + 1)) anios.Add(actual + 1);

        foreach (var p in periodos)
        {
            if (p.Anio < actual) continue;
            // El administrador puede en cualquier período creado, incluso cerrado
            if (u.IsInRole("SuperAdmin") || (Abierto(p) && await CalzaAsync(db, u, p))) anios.Add(p.Anio);
        }
        return anios.OrderBy(a => a).ToList();
    }

    /// <summary>Períodos abiertos donde esta persona puede matricular (para el aviso de arriba).</summary>
    public static async Task<List<Periodo>> AbiertosParaAsync(ApplicationDbContext db, ClaimsPrincipal u)
    {
        var res = new List<Periodo>();
        foreach (var p in (await CargarAsync(db, u)).Where(Abierto))
            if (await CalzaAsync(db, u, p)) res.Add(p);
        return res;
    }
}
