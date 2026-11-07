using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Períodos de matrícula por colegio (quién matricula y en qué fechas). Solo agrega una columna vacía:
    /// los colegios que ya funcionan siguen igual que antes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107130000_PeriodosMatricula")]
    public partial class PeriodosMatricula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "PeriodosMatricula", table: "Colegios", type: "text", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PeriodosMatricula", table: "Colegios");
        }
    }
}
