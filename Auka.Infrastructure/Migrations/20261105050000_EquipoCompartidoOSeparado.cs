using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Cómo trabaja el equipo psicosocial de cada organización: calendario y bitácora compartidos o separados,
    /// y fichas que solo ve su área (psicología o trabajo social). Todo parte compartido, como antes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261105050000_EquipoCompartidoOSeparado")]
    public partial class EquipoCompartidoOSeparado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CalendarioCompartido",
                table: "Colegios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BitacoraCompartida",
                table: "Colegios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "FichasPorArea",
                table: "Colegios",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CalendarioCompartido", table: "Colegios");
            migrationBuilder.DropColumn(name: "BitacoraCompartida", table: "Colegios");
            migrationBuilder.DropColumn(name: "FichasPorArea", table: "Colegios");
        }
    }
}
