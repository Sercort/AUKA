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

// El equipo psicosocial (con el módulo activo) va siempre a su panel, no al Home institucional
var rolesPsicosociales = new[] { "Psicologo", "Psicopedagogo", "TrabajadorSocial", "Orientadora" };
app.Use(async (context, next) =>
{
    var ruta = context.Request.Path.Value ?? "/";
    var esHome = ruta == "/" || ruta.Equals("/Home", StringComparison.OrdinalIgnoreCase) || ruta.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase);
    if (esHome
        && context.User.Identity?.IsAuthenticated == true
        && context.User.FindFirst("PlanPsicosocial")?.Value == "true"
        && rolesPsicosociales.Contains(context.User.FindFirst(ClaimTypes.Role)?.Value ?? ""))
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
