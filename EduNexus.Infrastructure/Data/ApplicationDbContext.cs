using EduNexus.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduNexus.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Tablas de la Base de Datos
    public DbSet<Colegio> Colegios { get; set; }
    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Curso> Cursos { get; set; }
    public DbSet<Asignatura> Asignaturas { get; set; }
    public DbSet<Estudiante> Estudiantes { get; set; }
    public DbSet<Apoderado> Apoderados { get; set; }
    public DbSet<EstudianteApoderado> EstudiantesApoderados { get; set; }
    public DbSet<Calificacion> Calificaciones { get; set; }
    public DbSet<Asistencia> Asistencias { get; set; }
    public DbSet<Anotacion> Anotaciones { get; set; }
    public DbSet<SolicitudEntrevista> SolicitudesEntrevistas { get; set; }
    public DbSet<ConversacionIA> ConversacionesIA { get; set; }
    public DbSet<HorarioClase> HorariosClases { get; set; }
    public DbSet<Evaluacion> Evaluaciones { get; set; }
    public DbSet<ContenidoEvaluacion> ContenidosEvaluaciones { get; set; }
    public DbSet<Taller> Talleres { get; set; }
    public DbSet<BloqueHorario> BloquesHorarios { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Relación N:M Estudiante-Apoderado
        modelBuilder.Entity<EstudianteApoderado>()
            .HasKey(ea => new { ea.EstudianteId, ea.ApoderadoId });

        modelBuilder.Entity<EstudianteApoderado>()
            .HasOne(ea => ea.Estudiante)
            .WithMany(e => e.Apoderados)
            .HasForeignKey(ea => ea.EstudianteId);

        modelBuilder.Entity<EstudianteApoderado>()
            .HasOne(ea => ea.Apoderado)
            .WithMany(a => a.Estudiantes)
            .HasForeignKey(ea => ea.ApoderadoId);

        // 2. Precisión decimal para Calificaciones
        modelBuilder.Entity<Calificacion>()
            .Property(c => c.Nota)
            .HasPrecision(3, 1);

        modelBuilder.Entity<Calificacion>()
            .Property(c => c.Ponderacion)
            .HasPrecision(5, 2);

        // 3. Desactivar borrado en cascada globalmente para evitar conflictos de "Multiple Cascade Paths"
        foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}