using Auka.Application.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace Auka.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly int _currentTenantId;

    // Constructor compatible con .NET 8 e inyección de dependencias[cite: 7]
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
        _currentTenantId = 1; // Tenant por defecto para desarrollo local[cite: 7]
    }

    // -----------------------------------------------------------------
    // Colecciones DbSet del Dominio Auka (Soporte completo para controladores)[cite: 7]
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
    public DbSet<Asistencia> Asistencias { get; set; } = null!; // 👈 REGISTRO REQUERIDO PARA EL DASHBOARD

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mapeo explícito de tablas en PostgreSQL[cite: 7]
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
        modelBuilder.Entity<Asistencia>().ToTable("Asistencias");

        // Mapeo de Clave Primaria para la relación intermedia EstudianteApoderado[cite: 7]
        modelBuilder.Entity<EstudianteApoderado>()
            .ToTable("EstudiantesApoderados")
            .HasKey(ea => ea.Id);

        // Aplicación SEGURA de Global Query Filters (Soft Delete y Multitenant)[cite: 7]
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 🛡️ EXCLUIR TALLER DEL FILTRO DINÁMICO (Previene la inyección de columnas inexistentes)[cite: 7]
            if (entityType.ClrType == typeof(Taller)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            Expression? filter = null;

            // 1. Filtro de Borrado Lógico (Solo si la entidad contiene explícitamente 'IsDeleted')[cite: 7]
            var isDeletedProp = entityType.FindProperty("IsDeleted");
            if (isDeletedProp != null && isDeletedProp.ClrType == typeof(bool))
            {
                var isDeletedProperty = Expression.Property(parameter, "IsDeleted");
                var isNotDeleted = Expression.Equal(isDeletedProperty, Expression.Constant(false));
                filter = isNotDeleted;
            }

            // 2. Filtro Multitenant (Solo si la entidad contiene explícitamente 'ColegioId')[cite: 7]
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
            // Interceptor de Borrado Físico -> Soft Delete[cite: 7]
            if (entry.State == EntityState.Deleted)
            {
                var isDeletedProp = entry.Metadata.FindProperty("IsDeleted");
                if (isDeletedProp != null)
                {
                    entry.State = EntityState.Modified;
                    entry.Property("IsDeleted").CurrentValue = true;
                }
            }

            // Interceptor de Auditoría de Fechas[cite: 7]
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