using Auka.Application.Entities;

namespace Auka.Infrastructure.Data;

public static class DbInitializer
{
    public static void Initialize(ApplicationDbContext context)
    {
        // Si ya existen usuarios, no hace nada
        if (context.Usuarios.Any())
        {
            return;
        }

        // 1. Crear Colegio Principal
        var colegio = new Colegio
        {
            Nombre = "Colegio San Francisco de Asís",
            Rut = "76.123.456-7",
            Direccion = "Av. Principal 123, Antofagasta",
            Telefono = "+56552123456",
            Activo = true,
            LatitudColegio = -23.6500,
            LongitudColegio = -70.4000
        };

        context.Colegios.Add(colegio);
        context.SaveChanges();

        // 2. Crear Usuarios (Incluyendo al SuperAdmin)
        var superAdmin = new Usuario
        {
            Rut = "11111111-1",
            Nombre = "Administrador",
            Apellido = "General",
            Email = "admin@Auka.cl",
            PasswordHash = "Admin123*",
            Rol = RolUsuario.SuperAdmin,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var director = new Usuario
        {
            Rut = "12345678-9",
            Nombre = "Carlos",
            Apellido = "Director",
            Email = "director@Auka.cl",
            PasswordHash = "Director123*",
            Rol = RolUsuario.Director,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var utp = new Usuario
        {
            Rut = "13456789-0",
            Nombre = "María",
            Apellido = "UTP",
            Email = "utp@Auka.cl",
            PasswordHash = "Utp123*",
            Rol = RolUsuario.UTP,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var psicopedagogo = new Usuario
        {
            Rut = "14567890-1",
            Nombre = "Laura",
            Apellido = "Psicopedagoga",
            Email = "pie@Auka.cl",
            PasswordHash = "Pie123*",
            Rol = RolUsuario.Psicopedagogo,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var inspector = new Usuario
        {
            Rut = "15678901-2",
            Nombre = "Roberto",
            Apellido = "Inspector",
            Email = "inspector@Auka.cl",
            PasswordHash = "Inspector123*",
            Rol = RolUsuario.Inspector,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var docente = new Usuario
        {
            Rut = "98765432-1",
            Nombre = "Ana",
            Apellido = "Pérez",
            Email = "docente@Auka.cl",
            PasswordHash = "Docente123*",
            Rol = RolUsuario.Docente,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var apoderadoUser = new Usuario
        {
            Rut = "11223344-5",
            Nombre = "Luis",
            Apellido = "Gómez",
            Email = "apoderado@Auka.cl",
            PasswordHash = "Apoderado123*",
            Rol = RolUsuario.Apoderado,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        var estudianteUser = new Usuario
        {
            Rut = "22334455-6",
            Nombre = "Matías",
            Apellido = "Gómez",
            Email = "estudiante@Auka.cl",
            PasswordHash = "Estudiante123*",
            Rol = RolUsuario.Estudiante,
            ColegioId = colegio.Id,
            Activo = true,
            FechaCreacion = DateTime.Now
        };

        context.Usuarios.AddRange(superAdmin, director, utp, psicopedagogo, inspector, docente, apoderadoUser, estudianteUser);
        context.SaveChanges();

        // 3. Crear Curso
        var curso = new Curso
        {
            Nombre = "Cuarto Medio",
            Letra = "A",
            AnioAcademico = 2026,
            ProfesorJefeId = docente.Id,
            ColegioId = colegio.Id
        };

        context.Cursos.Add(curso);
        context.SaveChanges();

        // 4. Perfiles
        var apoderado = new Apoderado
        {
            UsuarioId = apoderadoUser.Id,
            TelefonoContacto = "+56912345678"
        };
        context.Apoderados.Add(apoderado);

        var estudiante = new Estudiante
        {
            UsuarioId = estudianteUser.Id,
            CursoId = curso.Id
        };
        context.Estudiantes.Add(estudiante);
        context.SaveChanges();

        // Relación N:M
        context.EstudiantesApoderados.Add(new EstudianteApoderado
        {
            EstudianteId = estudiante.Id,
            ApoderadoId = apoderado.Id
        });

        // 5. Asignatura
        var asignatura = new Asignatura
        {
            Nombre = "Matemáticas",
            CursoId = curso.Id,
            DocenteId = docente.Id
        };
        context.Asignaturas.Add(asignatura);
        context.SaveChanges();
    }
}