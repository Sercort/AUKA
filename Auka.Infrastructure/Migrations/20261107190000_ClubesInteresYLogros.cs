using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Clubes: alumnos/as interesados, salidas por rendimiento (para avisar cuando mejoran), logros (trofeos y medallas)
    /// y cupo mínimo. Solo agrega una columna y tablas nuevas.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107190000_ClubesInteresYLogros")]
    public partial class ClubesInteresYLogros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "CupoMinimo", table: "Talleres", type: "integer", nullable: true);

            migrationBuilder.CreateTable(
                name: "InteresesTaller",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    TallerId = table.Column<int>(type: "integer", nullable: true),
                    Categoria = table.Column<string>(type: "text", nullable: true),
                    Comentario = table.Column<string>(type: "text", nullable: true),
                    RegistradoPorId = table.Column<int>(type: "integer", nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Atendido = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteresesTaller", x => x.Id);
                    table.ForeignKey(name: "FK_InteresesTaller_Estudiantes_EstudianteId", column: x => x.EstudianteId, principalTable: "Estudiantes", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_InteresesTaller_ColegioId_Atendido", table: "InteresesTaller", columns: new[] { "ColegioId", "Atendido" });
            migrationBuilder.CreateIndex(name: "IX_InteresesTaller_EstudianteId", table: "InteresesTaller", column: "EstudianteId");

            migrationBuilder.CreateTable(
                name: "SalidasTaller",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    TallerId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    Promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    PorId = table.Column<int>(type: "integer", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvisadoMejora = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_SalidasTaller", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_SalidasTaller_EstudianteId_AvisadoMejora", table: "SalidasTaller", columns: new[] { "EstudianteId", "AvisadoMejora" });

            migrationBuilder.CreateTable(
                name: "LogrosTaller",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    TallerId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Evento = table.Column<string>(type: "text", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false),
                    Detalle = table.Column<string>(type: "text", nullable: true),
                    RegistradoPorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_LogrosTaller", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_LogrosTaller_TallerId", table: "LogrosTaller", column: "TallerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InteresesTaller");
            migrationBuilder.DropTable(name: "SalidasTaller");
            migrationBuilder.DropTable(name: "LogrosTaller");
            migrationBuilder.DropColumn(name: "CupoMinimo", table: "Talleres");
        }
    }
}
