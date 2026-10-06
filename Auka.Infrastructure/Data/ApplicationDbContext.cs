using Auka.Application.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Auka.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly int _currentTenantId;

    // Constructor compatible con la inyección de dependencias de .NET 8
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
        _currentTenantId = 1; // ID de tenant base para desarrollo y pruebas locales
    }

    // -----------------------------------------------------------------
    // Colecciones DbSet del Dominio Auka (Soporte completo para controladores)
    // -----------------------------------------------------------------
    public DbSet<Colegio> Colegios { get; set; } = null!;
    public DbSet<Usuario> Usuarios { get; set; } = null!;
    public DbSet<Apoderado> Apoderados { get; set; } = null!;
    public DbSet<Estudiante> Estudiantes { get; set; } = null!;
    public DbSet<EstudianteApoderado> EstudiantesApoderados { get; set; } = null!;
    public DbSet<Curso> Cursos { get; set; } = null!;
    public DbSet<Asignatura> Asignaturas { get; set; } = null!;
    public DbSet<Evaluacion> Evaluaciones { get; set; } = null!;
    public DbSet<Calificacion> Calificaciones { get; set; } = null!;
    public DbSet<Taller> Talleres { get; set; } = null!;
    public DbSet<BloqueHorario> BloquesHorarios { get; set; } = null!;
    public DbSet<Anotacion> Anotaciones { get; set; } = null!;
    public DbSet<AnotacionEliminada> AnotacionesEliminadas { get; set; } = null!;
    public DbSet<BitacoraPsicosocial> BitacorasPsicosociales { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mapeo explícito de tablas en PostgreSQL
        modelBuilder.Entity<Colegio>().ToTable("Colegios");
        modelBuilder.Entity<Usuario>().ToTable("Usuarios");
        modelBuilder.Entity<Apoderado>().ToTable("Apoderados");
        modelBuilder.Entity<Estudiante>().ToTable("Estudiantes");
        modelBuilder.Entity<Curso>().ToTable("Cursos");
        modelBuilder.Entity<Asignatura>().ToTable("Asignaturas");
        modelBuilder.Entity<Evaluacion>().ToTable("Evaluaciones");
        modelBuilder.Entity<Calificacion>().ToTable("Calificaciones");
        modelBuilder.Entity<Taller>().ToTable("Talleres");
        modelBuilder.Entity<BloqueHorario>().ToTable("BloquesHorarios");
        modelBuilder.Entity<Anotacion>().ToTable("Anotaciones");
        modelBuilder.Entity<AnotacionEliminada>().ToTable("AnotacionesEliminadas");
        modelBuilder.Entity<BitacoraPsicosocial>().ToTable("BitacorasPsicosociales");

        // 🔑 Mapeo explícito de Clave Primaria para EstudianteApoderado
        modelBuilder.Entity<EstudianteApoderado>()
            .ToTable("EstudiantesApoderados")
            .HasKey(ea => ea.Id);

        // Aplicación automática de Global Query Filters (Soft Delete e IsDeleted)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            Expression? filter = null;

            // Filtro de Borrado Lógico (IsDeleted == false)
            var isDeletedProp = entityType.FindProperty("IsDeleted");
            if (isDeletedProp != null && isDeletedProp.ClrType == typeof(bool))
            {
                var isDeletedProperty = Expression.Property(parameter, "IsDeleted");
                var isNotDeleted = Expression.Equal(isDeletedProperty, Expression.Constant(false));
                filter = isNotDeleted;
            }

            // Filtro Multitenant por ColegioId
            var tenantProp = entityType.FindProperty("ColegioId");
            if (tenantProp != null && tenantProp.ClrType == typeof(int))
            {
                var tenantProperty = Expression.Property(parameter, "ColegioId");
                var tenantMatches = Expression.Equal(tenantProperty, Expression.Constant(_currentTenantId));
                filter = filter == null ? tenantMatches : Expression.AndAlso(filter, tenantMatches);
            }

            if (filter != null)
            {
                var lambda = Expression.Lambda(filter, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            // Interceptor de Borrado Físico -> Soft Delete
            if (entry.State == EntityState.Deleted)
            {
                var isDeletedProp = entry.Metadata.FindProperty("IsDeleted");
                if (isDeletedProp != null)
                {
                    entry.State = EntityState.Modified;
                    entry.Property("IsDeleted").CurrentValue = true;
                }
            }

            // Interceptor de Auditoría de Fechas
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                var fechaModificacion = entry.Metadata.FindProperty("FechaUltimaModificacion");
                if (fechaModificacion != null)
                {
                    entry.Property("FechaUltimaModificacion").CurrentValue = DateTime.UtcNow;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}