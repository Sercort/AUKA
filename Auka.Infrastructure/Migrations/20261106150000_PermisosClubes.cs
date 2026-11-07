using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>El administrador del colegio elige quién más puede crear clubes (UTP y/u orientadora).</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261106150000_PermisosClubes")]
    public partial class PermisosClubes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClubesPermitidos",
                table: "Colegios",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ClubesPermitidos", table: "Colegios");
        }
    }
}
