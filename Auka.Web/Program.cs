using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configuración de Entity Framework Core con PostgreSQL (Open Source)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configurar Autenticación por Cookies y Google SSO
builder.Services.AddAuthentication("AukaAuthCookie")
    .AddCookie("AukaAuthCookie", options =>
    {
        options.Cookie.Name = "Auka.Cookie";
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccesoDenegado";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "CLIENT_ID_TEMPORAL";
        googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "CLIENT_SECRET_TEMPORAL";
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
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

// --- SEED DATA INICIAL Y CREACIÓN DE BD ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Crear automáticamente la BD y tablas si no existen
        context.Database.EnsureCreated();
        DbInitializer.Initialize(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ocurrió un error al inicializar la base de datos PostgreSQL.");
    }
}

app.Run();