using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Rut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Apellido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rol = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Apoderados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    TelefonoContacto = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apoderados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Apoderados_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cursos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Letra = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnioAcademico = table.Column<int>(type: "int", nullable: false),
                    ProfesorJefeId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cursos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cursos_Usuarios_ProfesorJefeId",
                        column: x => x.ProfesorJefeId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SolicitudesEntrevistas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaHoraPropuesta = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    RespuestaDocente = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApoderadoId = table.Column<int>(type: "int", nullable: false),
                    DocenteId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesEntrevistas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitudesEntrevistas_Apoderados_ApoderadoId",
                        column: x => x.ApoderadoId,
                        principalTable: "Apoderados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SolicitudesEntrevistas_Usuarios_DocenteId",
                        column: x => x.DocenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Asignaturas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    DocenteId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asignaturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Asignaturas_Cursos_CursoId",
                        column: x => x.CursoId,
                        principalTable: "Cursos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Asignaturas_Usuarios_DocenteId",
                        column: x => x.DocenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Estudiantes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    CursoId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Estudiantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Estudiantes_Cursos_CursoId",
                        column: x => x.CursoId,
                        principalTable: "Cursos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Estudiantes_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Anotaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Detalle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    DocenteId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anotaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Anotaciones_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Anotaciones_Usuarios_DocenteId",
                        column: x => x.DocenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Asistencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Presente = table.Column<bool>(type: "bit", nullable: false),
                    LatitudCapturada = table.Column<double>(type: "float", nullable: true),
                    LongitudCapturada = table.Column<double>(type: "float", nullable: true),
                    EnRangoColegio = table.Column<bool>(type: "bit", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    AsignaturaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asistencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Asistencias_Asignaturas_AsignaturaId",
                        column: x => x.AsignaturaId,
                        principalTable: "Asignaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Asistencias_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Calificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nota = table.Column<decimal>(type: "decimal(3,1)", precision: 3, scale: 1, nullable: false),
                    Ponderacion = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    AsignaturaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calificaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calificaciones_Asignaturas_AsignaturaId",
                        column: x => x.AsignaturaId,
                        principalTable: "Asignaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Calificaciones_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConversacionesIA",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PreguntaEstudiante = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RespuestaIA = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    AsignaturaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversacionesIA", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversacionesIA_Asignaturas_AsignaturaId",
                        column: x => x.AsignaturaId,
                        principalTable: "Asignaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConversacionesIA_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstudiantesApoderados",
                columns: table => new
                {
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    ApoderadoId = table.Column<int>(type: "int", nullable: false),
                    Parentesco = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EsSostenedorPrincipal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstudiantesApoderados", x => new { x.EstudianteId, x.ApoderadoId });
                    table.ForeignKey(
                        name: "FK_EstudiantesApoderados_Apoderados_ApoderadoId",
                        column: x => x.ApoderadoId,
                        principalTable: "Apoderados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstudiantesApoderados_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Anotaciones_DocenteId",
                table: "Anotaciones",
                column: "DocenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Anotaciones_EstudianteId",
                table: "Anotaciones",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_Apoderados_UsuarioId",
                table: "Apoderados",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Asignaturas_CursoId",
                table: "Asignaturas",
                column: "CursoId");

            migrationBuilder.CreateIndex(
                name: "IX_Asignaturas_DocenteId",
                table: "Asignaturas",
                column: "DocenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_AsignaturaId",
                table: "Asistencias",
                column: "AsignaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_EstudianteId",
                table: "Asistencias",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_Calificaciones_AsignaturaId",
                table: "Calificaciones",
                column: "AsignaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Calificaciones_EstudianteId",
                table: "Calificaciones",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversacionesIA_AsignaturaId",
                table: "ConversacionesIA",
                column: "AsignaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversacionesIA_EstudianteId",
                table: "ConversacionesIA",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_Cursos_ProfesorJefeId",
                table: "Cursos",
                column: "ProfesorJefeId");

            migrationBuilder.CreateIndex(
                name: "IX_Estudiantes_CursoId",
                table: "Estudiantes",
                column: "CursoId");

            migrationBuilder.CreateIndex(
                name: "IX_Estudiantes_UsuarioId",
                table: "Estudiantes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_EstudiantesApoderados_ApoderadoId",
                table: "EstudiantesApoderados",
                column: "ApoderadoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesEntrevistas_ApoderadoId",
                table: "SolicitudesEntrevistas",
                column: "ApoderadoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesEntrevistas_DocenteId",
                table: "SolicitudesEntrevistas",
                column: "DocenteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Anotaciones");

            migrationBuilder.DropTable(
                name: "Asistencias");

            migrationBuilder.DropTable(
                name: "Calificaciones");

            migrationBuilder.DropTable(
                name: "ConversacionesIA");

            migrationBuilder.DropTable(
                name: "EstudiantesApoderados");

            migrationBuilder.DropTable(
                name: "SolicitudesEntrevistas");

            migrationBuilder.DropTable(
                name: "Asignaturas");

            migrationBuilder.DropTable(
                name: "Estudiantes");

            migrationBuilder.DropTable(
                name: "Apoderados");

            migrationBuilder.DropTable(
                name: "Cursos");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
