using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// "Iniciar clase" (clases en curso) y profesores/as de reemplazo que asigna la UTP. Solo tablas nuevas.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261109100000_ClasesIniciadas")]
    public partial class ClasesIniciadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SesionesClase",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    CursoId = table.Column<int>(type: "integer", nullable: false),
                    BloqueJornadaId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfesorId = table.Column<int>(type: "integer", nullable: false),
                    InicioUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reemplazo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_SesionesClase", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_SesionesClase_ColegioId_Fecha", table: "SesionesClase", columns: new[] { "ColegioId", "Fecha" });
            migrationBuilder.CreateIndex(name: "IX_SesionesClase_CursoId_BloqueJornadaId_Fecha", table: "SesionesClase", columns: new[] { "CursoId", "BloqueJornadaId", "Fecha" });

            migrationBuilder.CreateTable(
                name: "ReemplazosClase",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    CursoId = table.Column<int>(type: "integer", nullable: false),
                    BloqueJornadaId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProfesorId = table.Column<int>(type: "integer", nullable: false),
                    TitularId = table.Column<int>(type: "integer", nullable: true),
                    Motivo = table.Column<string>(type: "text", nullable: true),
                    AsignadoPorId = table.Column<int>(type: "integer", nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_ReemplazosClase", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_ReemplazosClase_CursoId_BloqueJornadaId_Fecha", table: "ReemplazosClase", columns: new[] { "CursoId", "BloqueJornadaId", "Fecha" });
            migrationBuilder.CreateIndex(name: "IX_ReemplazosClase_ProfesorId_Fecha", table: "ReemplazosClase", columns: new[] { "ProfesorId", "Fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SesionesClase");
            migrationBuilder.DropTable(name: "ReemplazosClase");
        }
    }
}
