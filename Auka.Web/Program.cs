using System.Security.Claims;
using Auka.Application.Interfaces;
using Auka.Application.Services;
using Auka.Infrastructure.Data;
using Auka.Infrastructure.Repositories;
using Auka.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Conexión nativa a PostgreSQL con Npgsql
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se encontró la cadena 'DefaultConnection' para PostgreSQL.");

// Multitenant: el colegio sale del claim "ColegioId" de la sesión
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();

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

builder.Services.AddControllersWithViews();

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
var rutasPlanPsicosocial = new[] { "/Administracion", "/Personal", "/Psychosocial", "/Account", "/Home/Error" };
app.Use(async (context, next) =>
{
    var ruta = context.Request.Path.Value ?? "/";
    var user = context.User;
    if (user.Identity?.IsAuthenticated != true) { await next(); return; }

    var rol = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
    var planPsico = user.FindFirst("PlanPsicosocial")?.Value == "true";
    var soloPsico = planPsico && user.FindFirst("PlanPremium")?.Value == "false";
    var esHome = ruta == "/" || ruta.Equals("/Home", StringComparison.OrdinalIgnoreCase) || ruta.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase);

    if (soloPsico)
    {
        var destino = rol == "SuperAdmin" ? "/Administracion" : "/Psychosocial";
        var permitida = rutasPlanPsicosocial.Any(p => ruta.StartsWith(p, StringComparison.OrdinalIgnoreCase));
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
