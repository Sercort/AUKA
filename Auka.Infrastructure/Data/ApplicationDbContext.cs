using Auka.Application.Entities;
using Auka.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Auka.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    // El colegio de la sesión llega por inyección de dependencias (ver HttpTenantProvider)
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    /// <summary>
    /// Colegio (tenant) de la sesión actual. EF Core lo lee en cada consulta, por eso el
    /// filtro global cambia de colegio sin recrear el modelo. 0 = sin sesión.
    /// </summary>
    public int CurrentTenantId => _tenantProvider.TenantId;

    // -----------------------------------------------------------------
    // Colecciones DbSet del Dominio Auka
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
    public DbSet<Asistencia> Asistencias { get; set; } = null!;
    public DbSet<EventoCalendario> EventosCalendario { get; set; } = null!;

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
        modelBuilder.Entity<Asistencia>().ToTable("Asistencias");
        modelBuilder.Entity<EventoCalendario>().ToTable("EventosCalendario");

        modelBuilder.Entity<EstudianteApoderado>()
            .ToTable("EstudiantesApoderados")
            .HasKey(ea => ea.Id);

        // Global Query Filters: Soft Delete + Multitenant (por ColegioId)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Taller se excluye del filtro dinámico (no tiene las columnas esperadas)
            if (entityType.ClrType == typeof(Taller)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            Expression? filter = null;

            // 1. Borrado lógico (solo si la entidad tiene 'IsDeleted')
            var isDeletedProp = entityType.FindProperty("IsDeleted");
            if (isDeletedProp != null && isDeletedProp.ClrType == typeof(bool))
            {
                var isDeletedProperty = Expression.Property(parameter, "IsDeleted");
                filter = Expression.Equal(isDeletedProperty, Expression.Constant(false));
            }

            // 2. Multitenant (solo si la entidad tiene 'ColegioId').
            //    IMPORTANTE: se compara contra una PROPIEDAD de este contexto, no contra una constante.
            //    Así el valor se evalúa en cada consulta y cada sesión ve solo su colegio.
            var tenantProp = entityType.FindProperty("ColegioId");
            if (tenantProp != null && tenantProp.ClrType == typeof(int))
            {
                var tenantProperty = Expression.Property(parameter, "ColegioId");
                var currentTenant = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
                var tenantMatches = Expression.Equal(tenantProperty, currentTenant);
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
        var tenantId = CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Todo registro nuevo queda asociado al colegio de la sesión (si no lo trae asignado)
            if (entry.State == EntityState.Added && tenantId > 0)
            {
                var colegioIdProp = entry.Metadata.FindProperty("ColegioId");
                if (colegioIdProp != null && colegioIdProp.ClrType == typeof(int)
                    && Equals(entry.Property("ColegioId").CurrentValue, 0))
                {
                    entry.Property("ColegioId").CurrentValue = tenantId;
                }
            }

            // Borrado físico -> Soft Delete
            if (entry.State == EntityState.Deleted)
            {
                var isDeletedProp = entry.Metadata.FindProperty("IsDeleted");
                if (isDeletedProp != null)
                {
                    entry.State = EntityState.Modified;
                    entry.Property("IsDeleted").CurrentValue = true;
                }
            }

            // Auditoría de fechas
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
