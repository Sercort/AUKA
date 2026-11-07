using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Clubes con horario real: días y horas (sesiones), lugar, para qué niveles y temporada.
    /// Solo agrega columnas vacías y una tabla nueva: los clubes que ya existen siguen igual.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107170000_ClubesHorario")]
    public partial class ClubesHorario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Lugar", table: "Talleres", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Niveles", table: "Talleres", type: "text", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "FechaInicio", table: "Talleres", type: "date", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "FechaFin", table: "Talleres", type: "date", nullable: true);

            migrationBuilder.CreateTable(
                name: "SesionesTaller",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    TallerId = table.Column<int>(type: "integer", nullable: false),
                    Dia = table.Column<int>(type: "integer", nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraFin = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SesionesTaller", x => x.Id);
                    table.ForeignKey(name: "FK_SesionesTaller_Talleres_TallerId", column: x => x.TallerId,
                        principalTable: "Talleres", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_SesionesTaller_ColegioId_Dia", table: "SesionesTaller", columns: new[] { "ColegioId", "Dia" });
            migrationBuilder.CreateIndex(name: "IX_SesionesTaller_TallerId", table: "SesionesTaller", column: "TallerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SesionesTaller");
            migrationBuilder.DropColumn(name: "Lugar", table: "Talleres");
            migrationBuilder.DropColumn(name: "Niveles", table: "Talleres");
            migrationBuilder.DropColumn(name: "FechaInicio", table: "Talleres");
            migrationBuilder.DropColumn(name: "FechaFin", table: "Talleres");
        }
    }
}
