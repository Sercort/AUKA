using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Colegios con especialidades técnico-profesionales desde 1º medio (no solo 3º y 4º).
    /// Solo agrega una columna con valor falso: los colegios que ya operan siguen igual.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261109070000_EspecialidadDesdePrimeroMedio")]
    public partial class EspecialidadDesdePrimeroMedio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "EspecialidadDesdePrimeroMedio", table: "Colegios", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EspecialidadDesdePrimeroMedio", table: "Colegios");
        }
    }
}
