using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Solicitudes del profesor/a a la UTP en su club (inscribir con el club lleno o de otro nivel, aumentar cupo).
    /// Solo agrega una tabla nueva: no toca datos existentes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261109050000_SolicitudesClub")]
    public partial class SolicitudesClub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SolicitudesClub",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    TallerId = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: true),
                    CupoPedido = table.Column<int>(type: "integer", nullable: true),
                    Bloqueo = table.Column<string>(type: "text", nullable: true),
                    Comentario = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    PedidaPorId = table.Column<int>(type: "integer", nullable: false),
                    PedidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResueltaPorId = table.Column<int>(type: "integer", nullable: true),
                    ResueltaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Respuesta = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_SolicitudesClub", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_SolicitudesClub_TallerId", table: "SolicitudesClub", column: "TallerId");
            migrationBuilder.CreateIndex(name: "IX_SolicitudesClub_ColegioId_Estado", table: "SolicitudesClub", columns: new[] { "ColegioId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SolicitudesClub");
        }
    }
}
