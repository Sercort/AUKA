using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>Marca "el profesor/a jefe no sigue con este curso el año siguiente". Por defecto sigue (como hasta ahora).</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108030000_JefeNoSigue")]
    public partial class JefeNoSigue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "JefeNoSigue", table: "Cursos", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "JefeNoSigue", table: "Cursos");
        }
    }
}
