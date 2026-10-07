using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarActivoATalleres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Anotaciones_Estudiantes_EstudianteId",
                table: "Anotaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Anotaciones_Usuarios_DocenteId",
                table: "Anotaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Apoderados_Usuarios_UsuarioId",
                table: "Apoderados");

            migrationBuilder.DropForeignKey(
                name: "FK_Asignaturas_Cursos_CursoId",
                table: "Asignaturas");

            migrationBuilder.DropForeignKey(
                name: "FK_Asignaturas_Usuarios_DocenteId",
                table: "Asignaturas");

            migrationBuilder.DropForeignKey(
                name: "FK_Calificaciones_Asignaturas_AsignaturaId",
                table: "Calificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Calificaciones_Estudiantes_EstudianteId",
                table: "Calificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Cursos_Colegios_ColegioId",
                table: "Cursos");

            migrationBuilder.DropForeignKey(
                name: "FK_Cursos_Usuarios_ProfesorJefeId",
                table: "Cursos");

            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Cursos_CursoId",
                table: "Estudiantes");

            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Talleres_TallerId",
                table: "Estudiantes");

            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Usuarios_UsuarioId",
                table: "Estudiantes");

            migrationBuilder.DropForeignKey(
                name: "FK_EstudiantesApoderados_Apoderados_ApoderadoId",
                table: "EstudiantesApoderados");

            migrationBuilder.DropForeignKey(
                name: "FK_EstudiantesApoderados_Estudiantes_EstudianteId",
                table: "EstudiantesApoderados");

            migrationBuilder.DropForeignKey(
                name: "FK_Evaluaciones_Asignaturas_AsignaturaId",
                table: "Evaluaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_Asignaturas_AsignaturaId",
                table: "HorariosClases");

            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_BloquesHorarios_BloqueHorarioId",
                table: "HorariosClases");

            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_Cursos_CursoId",
                table: "HorariosClases");

            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_Usuarios_ProfesorId",
                table: "HorariosClases");

            migrationBuilder.DropForeignKey(
                name: "FK_Talleres_Colegios_ColegioId",
                table: "Talleres");

            migrationBuilder.DropForeignKey(
                name: "FK_Talleres_Usuarios_DocenteCargoId",
                table: "Talleres");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Colegios_ColegioId",
                table: "Usuarios");

            migrationBuilder.DropTable(
                name: "Asistencias");

            migrationBuilder.DropTable(
                name: "ContenidosEvaluaciones");

            migrationBuilder.DropTable(
                name: "ConversacionesIA");

            migrationBuilder.DropTable(
                name: "SolicitudesEntrevistas");

            migrationBuilder.DropIndex(
                name: "IX_Talleres_ColegioId",
                table: "Talleres");

            migrationBuilder.DropIndex(
                name: "IX_Talleres_DocenteCargoId",
                table: "Talleres");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EstudiantesApoderados",
                table: "EstudiantesApoderados");

            migrationBuilder.DropPrimaryKey(
                name: "PK_HorariosClases",
                table: "HorariosClases");

            migrationBuilder.DropColumn(
                name: "DocenteCargoId",
                table: "Talleres");

            migrationBuilder.RenameTable(
                name: "HorariosClases",
                newName: "HorarioClase");

            migrationBuilder.RenameIndex(
                name: "IX_HorariosClases_ProfesorId",
                table: "HorarioClase",
                newName: "IX_HorarioClase_ProfesorId");

            migrationBuilder.RenameIndex(
                name: "IX_HorariosClases_CursoId",
                table: "HorarioClase",
                newName: "IX_HorarioClase_CursoId");

            migrationBuilder.RenameIndex(
                name: "IX_HorariosClases_BloqueHorarioId",
                table: "HorarioClase",
                newName: "IX_HorarioClase_BloqueHorarioId");

            migrationBuilder.RenameIndex(
                name: "IX_HorariosClases_AsignaturaId",
                table: "HorarioClase",
                newName: "IX_HorarioClase_AsignaturaId");

            migrationBuilder.AlterColumn<string>(
                name: "Horario",
                table: "Talleres",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Descripcion",
                table: "Talleres",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Talleres",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DocenteCargo",
                table: "Talleres",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Contenido",
                table: "Evaluaciones",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "CursoId",
                table: "Evaluaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Descripcion",
                table: "Evaluaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ponderacion",
                table: "Evaluaciones",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "EstudiantesApoderados",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<decimal>(
                name: "Ponderacion",
                table: "Calificaciones",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Nota",
                table: "Calificaciones",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(3,1)",
                oldPrecision: 3,
                oldScale: 1);

            migrationBuilder.AlterColumn<string>(
                name: "Descripcion",
                table: "Calificaciones",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "EvaluacionId",
                table: "Calificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_EstudiantesApoderados",
                table: "EstudiantesApoderados",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_HorarioClase",
                table: "HorarioClase",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "AnotacionesEliminadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AnotacionId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioEliminoId = table.Column<int>(type: "integer", nullable: false),
                    MotivoEliminacion = table.Column<string>(type: "text", nullable: false),
                    FechaEliminacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContenidoOriginal = table.Column<string>(type: "text", nullable: true),
                    TipoAnotacion = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnotacionesEliminadas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BitacorasPsicosociales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    ProfesionalId = table.Column<int>(type: "integer", nullable: false),
                    Observaciones = table.Column<string>(type: "text", nullable: false),
                    FechaAtencion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BitacorasPsicosociales", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluaciones_CursoId",
                table: "Evaluaciones",
                column: "CursoId");

            migrationBuilder.CreateIndex(
                name: "IX_EstudiantesApoderados_EstudianteId",
                table: "EstudiantesApoderados",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_Calificaciones_EvaluacionId",
                table: "Calificaciones",
                column: "EvaluacionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Anotaciones_Estudiantes_EstudianteId",
                table: "Anotaciones",
                column: "EstudianteId",
                principalTable: "Estudiantes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Anotaciones_Usuarios_DocenteId",
                table: "Anotaciones",
                column: "DocenteId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Apoderados_Usuarios_UsuarioId",
                table: "Apoderados",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Asignaturas_Cursos_CursoId",
                table: "Asignaturas",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Asignaturas_Usuarios_DocenteId",
                table: "Asignaturas",
                column: "DocenteId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Calificaciones_Asignaturas_AsignaturaId",
                table: "Calificaciones",
                column: "AsignaturaId",
                principalTable: "Asignaturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Calificaciones_Estudiantes_EstudianteId",
                table: "Calificaciones",
                column: "EstudianteId",
                principalTable: "Estudiantes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Calificaciones_Evaluaciones_EvaluacionId",
                table: "Calificaciones",
                column: "EvaluacionId",
                principalTable: "Evaluaciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Cursos_Colegios_ColegioId",
                table: "Cursos",
                column: "ColegioId",
                principalTable: "Colegios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Cursos_Usuarios_ProfesorJefeId",
                table: "Cursos",
                column: "ProfesorJefeId",
                principalTable: "Usuarios",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Cursos_CursoId",
                table: "Estudiantes",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Talleres_TallerId",
                table: "Estudiantes",
                column: "TallerId",
                principalTable: "Talleres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Usuarios_UsuarioId",
                table: "Estudiantes",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EstudiantesApoderados_Apoderados_ApoderadoId",
                table: "EstudiantesApoderados",
                column: "ApoderadoId",
                principalTable: "Apoderados",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EstudiantesApoderados_Estudiantes_EstudianteId",
                table: "EstudiantesApoderados",
                column: "EstudianteId",
                principalTable: "Estudiantes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Evaluaciones_Asignaturas_AsignaturaId",
                table: "Evaluaciones",
                column: "AsignaturaId",
                principalTable: "Asignaturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Evaluaciones_Cursos_CursoId",
                table: "Evaluaciones",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HorarioClase_Asignaturas_AsignaturaId",
                table: "HorarioClase",
                column: "AsignaturaId",
                principalTable: "Asignaturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HorarioClase_BloquesHorarios_BloqueHorarioId",
                table: "HorarioClase",
                column: "BloqueHorarioId",
                principalTable: "BloquesHorarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HorarioClase_Cursos_CursoId",
                table: "HorarioClase",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HorarioClase_Usuarios_ProfesorId",
                table: "HorarioClase",
                column: "ProfesorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Colegios_ColegioId",
                table: "Usuarios",
                column: "ColegioId",
                principalTable: "Colegios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Anotaciones_Estudiantes_EstudianteId",
                table: "Anotaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Anotaciones_Usuarios_DocenteId",
                table: "Anotaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Apoderados_Usuarios_UsuarioId",
                table: "Apoderados");

            migrationBuilder.DropForeignKey(
                name: "FK_Asignaturas_Cursos_CursoId",
                table: "Asignaturas");

            migrationBuilder.DropForeignKey(
                name: "FK_Asignaturas_Usuarios_DocenteId",
                table: "Asignaturas");

            migrationBuilder.DropForeignKey(
                name: "FK_Calificaciones_Asignaturas_AsignaturaId",
                table: "Calificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Calificaciones_Estudiantes_EstudianteId",
                table: "Calificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Calificaciones_Evaluaciones_EvaluacionId",
                table: "Calificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Cursos_Colegios_ColegioId",
                table: "Cursos");

            migrationBuilder.DropForeignKey(
                name: "FK_Cursos_Usuarios_ProfesorJefeId",
                table: "Cursos");

            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Cursos_CursoId",
                table: "Estudiantes");

            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Talleres_TallerId",
                table: "Estudiantes");

            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Usuarios_UsuarioId",
                table: "Estudiantes");

            migrationBuilder.DropForeignKey(
                name: "FK_EstudiantesApoderados_Apoderados_ApoderadoId",
                table: "EstudiantesApoderados");

            migrationBuilder.DropForeignKey(
                name: "FK_EstudiantesApoderados_Estudiantes_EstudianteId",
                table: "EstudiantesApoderados");

            migrationBuilder.DropForeignKey(
                name: "FK_Evaluaciones_Asignaturas_AsignaturaId",
                table: "Evaluaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Evaluaciones_Cursos_CursoId",
                table: "Evaluaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_HorarioClase_Asignaturas_AsignaturaId",
                table: "HorarioClase");

            migrationBuilder.DropForeignKey(
                name: "FK_HorarioClase_BloquesHorarios_BloqueHorarioId",
                table: "HorarioClase");

            migrationBuilder.DropForeignKey(
                name: "FK_HorarioClase_Cursos_CursoId",
                table: "HorarioClase");

            migrationBuilder.DropForeignKey(
                name: "FK_HorarioClase_Usuarios_ProfesorId",
                table: "HorarioClase");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Colegios_ColegioId",
                table: "Usuarios");

            migrationBuilder.DropTable(
                name: "AnotacionesEliminadas");

            migrationBuilder.DropTable(
                name: "BitacorasPsicosociales");

            migrationBuilder.DropIndex(
                name: "IX_Evaluaciones_CursoId",
                table: "Evaluaciones");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EstudiantesApoderados",
                table: "EstudiantesApoderados");

            migrationBuilder.DropIndex(
                name: "IX_EstudiantesApoderados_EstudianteId",
                table: "EstudiantesApoderados");

            migrationBuilder.DropIndex(
                name: "IX_Calificaciones_EvaluacionId",
                table: "Calificaciones");

            migrationBuilder.DropPrimaryKey(
                name: "PK_HorarioClase",
                table: "HorarioClase");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Talleres");

            migrationBuilder.DropColumn(
                name: "DocenteCargo",
                table: "Talleres");

            migrationBuilder.DropColumn(
                name: "CursoId",
                table: "Evaluaciones");

            migrationBuilder.DropColumn(
                name: "Descripcion",
                table: "Evaluaciones");

            migrationBuilder.DropColumn(
                name: "Ponderacion",
                table: "Evaluaciones");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "EstudiantesApoderados");

            migrationBuilder.DropColumn(
                name: "EvaluacionId",
                table: "Calificaciones");

            migrationBuilder.RenameTable(
                name: "HorarioClase",
                newName: "HorariosClases");

            migrationBuilder.RenameIndex(
                name: "IX_HorarioClase_ProfesorId",
                table: "HorariosClases",
                newName: "IX_HorariosClases_ProfesorId");

            migrationBuilder.RenameIndex(
                name: "IX_HorarioClase_CursoId",
                table: "HorariosClases",
                newName: "IX_HorariosClases_CursoId");

            migrationBuilder.RenameIndex(
                name: "IX_HorarioClase_BloqueHorarioId",
                table: "HorariosClases",
                newName: "IX_HorariosClases_BloqueHorarioId");

            migrationBuilder.RenameIndex(
                name: "IX_HorarioClase_AsignaturaId",
                table: "HorariosClases",
                newName: "IX_HorariosClases_AsignaturaId");

            migrationBuilder.AlterColumn<string>(
                name: "Horario",
                table: "Talleres",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Descripcion",
                table: "Talleres",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocenteCargoId",
                table: "Talleres",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Contenido",
                table: "Evaluaciones",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Ponderacion",
                table: "Calificaciones",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "Nota",
                table: "Calificaciones",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "Descripcion",
                table: "Calificaciones",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_EstudiantesApoderados",
                table: "EstudiantesApoderados",
                columns: new[] { "EstudianteId", "ApoderadoId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_HorariosClases",
                table: "HorariosClases",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Asistencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AsignaturaId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    EnRangoColegio = table.Column<bool>(type: "boolean", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LatitudCapturada = table.Column<double>(type: "double precision", nullable: true),
                    LongitudCapturada = table.Column<double>(type: "double precision", nullable: true),
                    Presente = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "ContenidosEvaluaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocenteId = table.Column<int>(type: "integer", nullable: false),
                    EvaluacionId = table.Column<int>(type: "integer", nullable: false),
                    DetalleContenido = table.Column<string>(type: "text", nullable: false),
                    FechaPublicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContenidosEvaluaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContenidosEvaluaciones_Evaluaciones_EvaluacionId",
                        column: x => x.EvaluacionId,
                        principalTable: "Evaluaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContenidosEvaluaciones_Usuarios_DocenteId",
                        column: x => x.DocenteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConversacionesIA",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AsignaturaId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreguntaEstudiante = table.Column<string>(type: "text", nullable: false),
                    RespuestaIA = table.Column<string>(type: "text", nullable: false)
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
                name: "SolicitudesEntrevistas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApoderadoId = table.Column<int>(type: "integer", nullable: false),
                    DocenteId = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    FechaHoraPropuesta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    RespuestaDocente = table.Column<string>(type: "text", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_Talleres_ColegioId",
                table: "Talleres",
                column: "ColegioId");

            migrationBuilder.CreateIndex(
                name: "IX_Talleres_DocenteCargoId",
                table: "Talleres",
                column: "DocenteCargoId");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_AsignaturaId",
                table: "Asistencias",
                column: "AsignaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_EstudianteId",
                table: "Asistencias",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContenidosEvaluaciones_DocenteId",
                table: "ContenidosEvaluaciones",
                column: "DocenteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContenidosEvaluaciones_EvaluacionId",
                table: "ContenidosEvaluaciones",
                column: "EvaluacionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversacionesIA_AsignaturaId",
                table: "ConversacionesIA",
                column: "AsignaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversacionesIA_EstudianteId",
                table: "ConversacionesIA",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesEntrevistas_ApoderadoId",
                table: "SolicitudesEntrevistas",
                column: "ApoderadoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesEntrevistas_DocenteId",
                table: "SolicitudesEntrevistas",
                column: "DocenteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Anotaciones_Estudiantes_EstudianteId",
                table: "Anotaciones",
                column: "EstudianteId",
                principalTable: "Estudiantes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Anotaciones_Usuarios_DocenteId",
                table: "Anotaciones",
                column: "DocenteId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Apoderados_Usuarios_UsuarioId",
                table: "Apoderados",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Asignaturas_Cursos_CursoId",
                table: "Asignaturas",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Asignaturas_Usuarios_DocenteId",
                table: "Asignaturas",
                column: "DocenteId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Calificaciones_Asignaturas_AsignaturaId",
                table: "Calificaciones",
                column: "AsignaturaId",
                principalTable: "Asignaturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Calificaciones_Estudiantes_EstudianteId",
                table: "Calificaciones",
                column: "EstudianteId",
                principalTable: "Estudiantes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cursos_Colegios_ColegioId",
                table: "Cursos",
                column: "ColegioId",
                principalTable: "Colegios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cursos_Usuarios_ProfesorJefeId",
                table: "Cursos",
                column: "ProfesorJefeId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Cursos_CursoId",
                table: "Estudiantes",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Talleres_TallerId",
                table: "Estudiantes",
                column: "TallerId",
                principalTable: "Talleres",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Usuarios_UsuarioId",
                table: "Estudiantes",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EstudiantesApoderados_Apoderados_ApoderadoId",
                table: "EstudiantesApoderados",
                column: "ApoderadoId",
                principalTable: "Apoderados",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EstudiantesApoderados_Estudiantes_EstudianteId",
                table: "EstudiantesApoderados",
                column: "EstudianteId",
                principalTable: "Estudiantes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Evaluaciones_Asignaturas_AsignaturaId",
                table: "Evaluaciones",
                column: "AsignaturaId",
                principalTable: "Asignaturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_Asignaturas_AsignaturaId",
                table: "HorariosClases",
                column: "AsignaturaId",
                principalTable: "Asignaturas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_BloquesHorarios_BloqueHorarioId",
                table: "HorariosClases",
                column: "BloqueHorarioId",
                principalTable: "BloquesHorarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_Cursos_CursoId",
                table: "HorariosClases",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_Usuarios_ProfesorId",
                table: "HorariosClases",
                column: "ProfesorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Talleres_Colegios_ColegioId",
                table: "Talleres",
                column: "ColegioId",
                principalTable: "Colegios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Talleres_Usuarios_DocenteCargoId",
                table: "Talleres",
                column: "DocenteCargoId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Colegios_ColegioId",
                table: "Usuarios",
                column: "ColegioId",
                principalTable: "Colegios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
