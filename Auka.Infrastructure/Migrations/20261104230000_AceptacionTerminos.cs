using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Ley 21.719: guarda cuándo y qué versión de la Política de privacidad y los Términos aceptó cada usuario.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104230000_AceptacionTerminos")]
    public partial class AceptacionTerminos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AceptoTerminosUtc",
                table: "Usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VersionTerminosAceptada",
                table: "Usuarios",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AceptoTerminosUtc", table: "Usuarios");
            migrationBuilder.DropColumn(name: "VersionTerminosAceptada", table: "Usuarios");
        }
    }
}
