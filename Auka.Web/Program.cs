using System.Security.Claims;
using Auka.Application.Interfaces;
using Auka.Application.Services;
using Auka.Infrastructure.Data;
using Auka.Infrastructure.Repositories;
using Auka.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;   // TryGetValue / Set de IMemoryCache (aceptación de términos)

var builder = WebApplication.CreateBuilder(args);

// 1. Conexión nativa a PostgreSQL con Npgsql
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se encontró la cadena 'DefaultConnection' para PostgreSQL.");

// Multitenant: el colegio sale del claim "ColegioId" de la sesión
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();

// Avisos: dentro de la plataforma y por correo (configuración en la sección "Correo")
builder.Services.Configure<CorreoOptions>(builder.Configuration.GetSection("Correo"));
builder.Services.AddScoped<ICorreoService, SmtpCorreoService>();
builder.Services.AddScoped<INotificacionService, NotificacionService>();
builder.Services.AddHostedService<ReintentoCorreosService>();
builder.Services.AddHostedService<AvisoVencimientosService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly("Auka.Infrastructure");
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
    }));

// 2. Inyección de Patrones de Servicio de Auka
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IAnotacionService, AnotacionService>();

// 3. Autenticación de Sesión Nativa de Auka
builder.Services.AddAuthentication("AukaAuthCookie")
    .AddCookie("AukaAuthCookie", options =>
    {
        options.Cookie.Name = "Auka.Session";
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccesoDenegado";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// 3.1 Políticas de autorización
builder.Services.AddAuthorization(options =>
{
    // Solo colegios con el módulo Psicosocial activo
    options.AddPolicy("PsicosocialPremium", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("PlanPsicosocial", "true"));

    // Administrador de un colegio con Plan Psicosocial (sin Premium)
    options.AddPolicy("AdminPsicosocial", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("SuperAdmin")
              .RequireClaim("PlanPsicosocial", "true")
              .RequireClaim("PlanPremium", "false"));

    // Alumnos/as y apoderados/as: portafolio psicosocial (lo que el equipo comparte con ellos/as)
    options.AddPolicy("FamiliaPsicosocial", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("Estudiante", "Apoderado", "Docente")
              .RequireClaim("PlanPsicosocial", "true"));

    // Quien puede usar los servicios del módulo: el equipo psicosocial o el administrador del Plan Psicosocial
    options.AddPolicy("AccesoPsicosocial", policy =>
        policy.RequireAssertion(ctx =>
        {
            var u = ctx.User;
            if (u.Identity?.IsAuthenticated != true) return false;
            if (u.FindFirst("PlanPsicosocial")?.Value != "true") return false;
            var rol = u.FindFirst(ClaimTypes.Role)?.Value ?? "";
            if (rol is "Psicologo" or "Psicopedagogo" or "TrabajadorSocial" or "Orientadora") return true;
            return rol == "SuperAdmin" && u.FindFirst("PlanPremium")?.Value == "false";
        }));

    // Equipo psicosocial: el rol correcto Y el colegio con el módulo activo
    options.AddPolicy("EquipoPsicosocial", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("Psicologo", "Psicopedagogo", "TrabajadorSocial", "Orientadora")
              .RequireClaim("PlanPsicosocial", "true"));
});

// OrganizacionFilter: cada vista sabe si la organización es colegio (alumnos/as) u otra (pacientes)
builder.Services.AddScoped<Auka.Web.Services.OrganizacionFilter>();
builder.Services.AddControllersWithViews(o => o.Filters.AddService<Auka.Web.Services.OrganizacionFilter>());

var app = builder.Build();

// 4. Canalización Middleware de Producción
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Reglas de plan:
//  - Equipo psicosocial (con el módulo activo) va siempre a su panel, no al Home institucional.
//  - Plan Psicosocial (sin Premium): solo existen las secciones del módulo; todo lo demás se bloquea.
var rolesPsicosociales = new[] { "Psicologo", "Psicopedagogo", "TrabajadorSocial", "Orientadora" };
var rutasPlanPsicosocial = new[] { "/Administracion", "/Personal", "/Psychosocial", "/Notificaciones", "/Account", "/Legal", "/Home/Error" };
var rutasFamilia = new[] { "/Portafolio", "/Notificaciones", "/Account", "/Legal", "/Home/Error" };
app.Use(async (context, next) =>
{
    var ruta = context.Request.Path.Value ?? "/";
    var user = context.User;
    if (user.Identity?.IsAuthenticated != true) { await next(); return; }

    var rol = user.FindFirst(ClaimTypes.Role)?.Value ?? "";

    // El dueño de la plataforma no depende del plan de ningún colegio: va siempre a su panel
    if (rol == "AdminPlataforma")
    {
        var esRaiz = ruta == "/" || ruta.StartsWith("/Home", StringComparison.OrdinalIgnoreCase);
        if (esRaiz) { context.Response.Redirect("/Plataforma"); return; }
        await next();
        return;
    }

    var planPsico = user.FindFirst("PlanPsicosocial")?.Value == "true";
    var soloPsico = planPsico && user.FindFirst("PlanPremium")?.Value == "false";
    var esHome = ruta == "/" || ruta.Equals("/Home", StringComparison.OrdinalIgnoreCase) || ruta.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase);

    if (soloPsico)
    {
        var esFamilia = rol is "Estudiante" or "Apoderado" or "Docente"; // Docente = profesor/a jefe
        var destino = esFamilia ? "/Portafolio" : rol == "SuperAdmin" ? "/Administracion" : "/Psychosocial";
        var permitida = (esFamilia ? rutasFamilia : rutasPlanPsicosocial).Any(p => ruta.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        var soloAdmin = ruta.StartsWith("/Administracion", StringComparison.OrdinalIgnoreCase) || ruta.StartsWith("/Personal", StringComparison.OrdinalIgnoreCase);
        if (esHome || !permitida || (soloAdmin && rol != "SuperAdmin"))
        {
            context.Response.Redirect(destino);
            return;
        }
    }
    else if (esHome && planPsico && rolesPsicosociales.Contains(rol))
    {
        context.Response.Redirect("/Psychosocial");
        return;
    }
    await next();
});

// Ley 21.719: nadie usa AUKA sin aceptar la Política de privacidad y los Términos (se pide una vez por versión)
app.Use(async (context, next) =>
{
    var ruta = context.Request.Path.Value ?? "/";
    var user = context.User;
    var esPagina = HttpMethods.IsGet(context.Request.Method) &&
                   (context.Request.Headers.Accept.ToString().Contains("text/html") || string.IsNullOrEmpty(context.Request.Headers.Accept));
    if (user.Identity?.IsAuthenticated != true || !esPagina ||
        ruta.StartsWith("/Legal", StringComparison.OrdinalIgnoreCase) || ruta.StartsWith("/Account", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid))
    {
        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var llave = $"terminos:{uid}:{Auka.Web.Controllers.LegalController.Version}";
        if (!cache.TryGetValue(llave, out bool acepto))
        {
            var db = context.RequestServices.GetRequiredService<Auka.Infrastructure.Data.ApplicationDbContext>();
            acepto = await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(u => u.Id == uid && u.VersionTerminosAceptada == Auka.Web.Controllers.LegalController.Version);
            if (acepto) cache.Set(llave, true, TimeSpan.FromHours(12));
        }
        if (!acepto)
        {
            context.Response.Redirect("/Legal/Aceptar?volver=" + Uri.EscapeDataString(ruta + context.Request.QueryString));
            return;
        }
    }
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
