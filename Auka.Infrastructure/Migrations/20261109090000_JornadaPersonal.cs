using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Jornada en el colegio del personal sin clases (director/a, UTP, administrador...) y el botón "Estoy en el colegio".
    /// Solo agrega columnas vacías: no toca datos existentes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261109090000_JornadaPersonal")]
    public partial class JornadaPersonal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "JornadaPersonal", table: "Usuarios", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "EnColegio", table: "Usuarios", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "EnColegioUtc", table: "Usuarios", type: "timestamp with time zone", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "JornadaPersonal", table: "Usuarios");
            migrationBuilder.DropColumn(name: "EnColegio", table: "Usuarios");
            migrationBuilder.DropColumn(name: "EnColegioUtc", table: "Usuarios");
        }
    }
}
