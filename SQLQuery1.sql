UPDATE Usuarios
SET PasswordHash = 'Clave123*', -- O el Hash generado si usas PasswordHasher
    DebeCambiarPassword = 0,
    Activo = 1
WHERE Email = 'director@edunexus.cl' -- Reemplaza por tu correo de administrador
   OR Rol = 0; -- O el ID correspondiente al rol Administrador / Director