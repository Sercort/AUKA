using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Globalización de Auka Psicosocial: tipo de organización (Colegio, Empresa, Instituto, Fundación, Particular),
    /// planillas activas por organización y datos de contacto del paciente (dirección, teléfono, correo).
    /// Los colegios que ya existen quedan como "Colegio" con todas sus planillas (null = todas).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104190000_OrganizacionesYPacientes")]
    public partial class OrganizacionesYPacientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TipoOrganizacion",
                table: "Colegios",
                type: "text",
                nullable: false,
                defaultValue: "Colegio");

            migrationBuilder.AddColumn<string>(
                name: "PlanillasActivas",
                table: "Colegios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direccion",
                table: "Estudiantes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefono",
                table: "Estudiantes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorreoContacto",
                table: "Estudiantes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TipoOrganizacion", table: "Colegios");
            migrationBuilder.DropColumn(name: "PlanillasActivas", table: "Colegios");
            migrationBuilder.DropColumn(name: "Direccion", table: "Estudiantes");
            migrationBuilder.DropColumn(name: "Telefono", table: "Estudiantes");
            migrationBuilder.DropColumn(name: "CorreoContacto", table: "Estudiantes");
        }
    }
}
