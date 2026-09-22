using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuloHorariosYPruebas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColegioId",
                table: "Usuarios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ColegioId",
                table: "Cursos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Colegios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Direccion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    LatitudColegio = table.Column<double>(type: "float", nullable: false),
                    LongitudColegio = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Colegios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Evaluaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AsignaturaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evaluaciones_Asignaturas_AsignaturaId",
                        column: x => x.AsignaturaId,
                        principalTable: "Asignaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HorariosClases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiaSemana = table.Column<int>(type: "int", nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "time", nullable: false),
                    HoraFin = table.Column<TimeSpan>(type: "time", nullable: false),
                    Sala = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AsignaturaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorariosClases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HorariosClases_Asignaturas_AsignaturaId",
                        column: x => x.AsignaturaId,
                        principalTable: "Asignaturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContenidosEvaluaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetalleContenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaPublicacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EvaluacionId = table.Column<int>(type: "int", nullable: false),
                    DocenteId = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_ColegioId",
                table: "Usuarios",
                column: "ColegioId");

            migrationBuilder.CreateIndex(
                name: "IX_Cursos_ColegioId",
                table: "Cursos",
                column: "ColegioId");

            migrationBuilder.CreateIndex(
                name: "IX_ContenidosEvaluaciones_DocenteId",
                table: "ContenidosEvaluaciones",
                column: "DocenteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContenidosEvaluaciones_EvaluacionId",
                table: "ContenidosEvaluaciones",
                column: "EvaluacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluaciones_AsignaturaId",
                table: "Evaluaciones",
                column: "AsignaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_HorariosClases_AsignaturaId",
                table: "HorariosClases",
                column: "AsignaturaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cursos_Colegios_ColegioId",
                table: "Cursos",
                column: "ColegioId",
                principalTable: "Colegios",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cursos_Colegios_ColegioId",
                table: "Cursos");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Colegios_ColegioId",
                table: "Usuarios");

            migrationBuilder.DropTable(
                name: "Colegios");

            migrationBuilder.DropTable(
                name: "ContenidosEvaluaciones");

            migrationBuilder.DropTable(
                name: "HorariosClases");

            migrationBuilder.DropTable(
                name: "Evaluaciones");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_ColegioId",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Cursos_ColegioId",
                table: "Cursos");

            migrationBuilder.DropColumn(
                name: "ColegioId",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "ColegioId",
                table: "Cursos");
        }
    }
}
