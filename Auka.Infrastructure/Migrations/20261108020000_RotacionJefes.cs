using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>Opción del colegio: al terminar el ciclo, el profesor/a jefe mantiene su letra o rota. Por defecto mantiene (como hasta ahora).</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108020000_RotacionJefes")]
    public partial class RotacionJefes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "RotarLetraJefes", table: "Colegios", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RotarLetraJefes", table: "Colegios");
        }
    }
}
