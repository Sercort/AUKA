using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>Metas del año que fija la dirección del colegio. Solo agrega una columna vacía.</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261109120000_MetasDireccion")]
    public partial class MetasDireccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "MetasDireccion", table: "Colegios", type: "text", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MetasDireccion", table: "Colegios");
        }
    }
}
