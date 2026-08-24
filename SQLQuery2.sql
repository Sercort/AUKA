UPDATE dbo.Usuarios
SET PasswordHash = 'Estudiante123*'
WHERE Email IN ('tomas.rojas@colegiolosandes.cl', 'amanda.sepulveda@colegiolosandes.cl');