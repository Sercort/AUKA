using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Crea la tabla RegistrosPsicosociales: los registros de "+ Nuevo registro" de la Planilla Psicosocial
    /// (seguimiento, judicial, informes, visitas, derivaciones, plan, beneficios, ropa, egresos).
    /// Los campos de cada ficha se guardan en una columna jsonb.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104183000_RegistrosPsicosociales")]
    public partial class RegistrosPsicosociales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrosPsicosociales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: true),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DatosJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreadoPorId = table.Column<int>(type: "integer", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModificadoPorId = table.Column<int>(type: "integer", nullable: true),
                    FechaUltimaModificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrosPsicosociales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrosPsicosociales_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosPsicosociales_ColegioId_Tipo",
                table: "RegistrosPsicosociales",
                columns: new[] { "ColegioId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosPsicosociales_EstudianteId",
                table: "RegistrosPsicosociales",
                column: "EstudianteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrosPsicosociales");
        }
    }
}
