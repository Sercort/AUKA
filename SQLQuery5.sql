-- 1. Insertar Apoderada Claudia Muñoz Rojas
IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Email = 'claudia.munoz@colegiolosandes.cl')
BEGIN
    INSERT INTO Usuarios (Rut, Nombre, Apellido, Email, PasswordHash, Rol, ColegioId, Activo, FechaCreacion)
    VALUES ('12345678-K', 'Claudia', 'Muñoz Rojas', 'claudia.munoz@colegiolosandes.cl', 'Clave123*', 3, 1, 1, GETDATE());
END

-- 2. Insertar Usuarios Estudiantes (Hermanos)
IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Email = 'josefa.rojas@colegiolosandes.cl')
    INSERT INTO Usuarios (Rut, Nombre, Apellido, Email, PasswordHash, Rol, ColegioId, Activo, FechaCreacion)
    VALUES ('21111111-2', 'Josefa', 'Rojas Soto', 'josefa.rojas@colegiolosandes.cl', 'Clave123*', 4, 1, 1, GETDATE());

IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Email = 'martina.rojas@colegiolosandes.cl')
    INSERT INTO Usuarios (Rut, Nombre, Apellido, Email, PasswordHash, Rol, ColegioId, Activo, FechaCreacion)
    VALUES ('21111111-3', 'Martina', 'Rojas Soto', 'martina.rojas@colegiolosandes.cl', 'Clave123*', 4, 1, 1, GETDATE());

IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Email = 'lucas.rojas@colegiolosandes.cl')
    INSERT INTO Usuarios (Rut, Nombre, Apellido, Email, PasswordHash, Rol, ColegioId, Activo, FechaCreacion)
    VALUES ('21111111-4', 'Lucas', 'Rojas Soto', 'lucas.rojas@colegiolosandes.cl', 'Clave123*', 4, 1, 1, GETDATE());

-- 3. Asignar los perfiles de Estudiantes vinculados a sus respectivos Cursos
DECLARE @CursoTomas INT = (SELECT TOP 1 Id FROM Cursos WHERE Nombre = '4º Medio TP - Programación');
DECLARE @CursoJosefa INT = (SELECT TOP 1 Id FROM Cursos WHERE Nombre = '2º Medio');
DECLARE @CursoMartina INT = (SELECT TOP 1 Id FROM Cursos WHERE Nombre = '8º Básico');
DECLARE @CursoLucas INT = (SELECT TOP 1 Id FROM Cursos WHERE Nombre = '5º Básico');

-- Asignar Curso a Tomás si ya existe en Estudiantes
UPDATE Estudiantes SET CursoId = @CursoTomas WHERE UsuarioId IN (SELECT Id FROM Usuarios WHERE Email = 'tomas.rojas@colegiolosandes.cl');

-- Crear registros en Estudiantes para Josefa, Martina y Lucas
INSERT INTO Estudiantes (UsuarioId, CursoId, TipoMatricula, FechaMatricula)
SELECT Id, @CursoJosefa, 0, GETDATE() FROM Usuarios WHERE Email = 'josefa.rojas@colegiolosandes.cl'
AND NOT EXISTS (SELECT 1 FROM Estudiantes WHERE UsuarioId = Usuarios.Id);

INSERT INTO Estudiantes (UsuarioId, CursoId, TipoMatricula, FechaMatricula)
SELECT Id, @CursoMartina, 0, GETDATE() FROM Usuarios WHERE Email = 'martina.rojas@colegiolosandes.cl'
AND NOT EXISTS (SELECT 1 FROM Estudiantes WHERE UsuarioId = Usuarios.Id);

INSERT INTO Estudiantes (UsuarioId, CursoId, TipoMatricula, FechaMatricula)
SELECT Id, @CursoLucas, 0, GETDATE() FROM Usuarios WHERE Email = 'lucas.rojas@colegiolosandes.cl'
AND NOT EXISTS (SELECT 1 FROM Estudiantes WHERE UsuarioId = Usuarios.Id);