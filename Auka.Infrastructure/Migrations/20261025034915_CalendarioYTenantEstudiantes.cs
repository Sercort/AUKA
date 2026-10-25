using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CalendarioYTenantEstudiantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColegioId",
                table: "Estudiantes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaNacimiento",
                table: "Estudiantes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ColegioId",
                table: "BitacorasPsicosociales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaUltimaModificacion",
                table: "BitacorasPsicosociales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModificadoPorId",
                table: "BitacorasPsicosociales",
                type: "integer",
                nullable: true);

            // Completa el colegio de las alumnas y bitácoras que ya existían
            // (sin esto quedarían con ColegioId = 0 y el filtro multitenant las ocultaría)
            migrationBuilder.Sql(@"UPDATE ""Estudiantes"" e SET ""ColegioId"" = c.""ColegioId"" FROM ""Cursos"" c WHERE e.""CursoId"" = c.""Id"";");
            migrationBuilder.Sql(@"UPDATE ""BitacorasPsicosociales"" b SET ""ColegioId"" = e.""ColegioId"" FROM ""Estudiantes"" e WHERE b.""EstudianteId"" = e.""Id"";");

            migrationBuilder.CreateTable(
                name: "EventosCalendario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Hora = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Responsable = table.Column<string>(type: "text", nullable: false),
                    Realizada = table.Column<bool>(type: "boolean", nullable: false),
                    CreadoPorId = table.Column<int>(type: "integer", nullable: false),
                    CreadoEnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModificadoPorId = table.Column<int>(type: "integer", nullable: true),
                    FechaUltimaModificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosCalendario", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosCalendario");

            migrationBuilder.DropColumn(
                name: "ColegioId",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "FechaNacimiento",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "ColegioId",
                table: "BitacorasPsicosociales");

            migrationBuilder.DropColumn(
                name: "FechaUltimaModificacion",
                table: "BitacorasPsicosociales");

            migrationBuilder.DropColumn(
                name: "ModificadoPorId",
                table: "BitacorasPsicosociales");
        }
    }
}