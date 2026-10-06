using Auka.Application.Interfaces;
using Auka.Application.Services;
using Auka.Infrastructure.Data;
using Auka.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurar conexión nativa a PostgreSQL con Npgsql
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se encontró la cadena 'DefaultConnection' para PostgreSQL.");

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
    var tenantClaim = httpContextAccessor.HttpContext?.User.FindFirst("ColegioId")?.Value;
    int tenantId = int.TryParse(tenantClaim, out var id) ? id : 0;

    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly("Auka.Infrastructure");
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
    });
});

builder.Services.AddHttpContextAccessor();

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();