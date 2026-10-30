using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlataformaAdministracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactoCorreo",
                table: "Colegios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactoNombre",
                table: "Colegios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactoTelefono",
                table: "Colegios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaBaja",
                table: "Colegios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaRegistro",
                table: "Colegios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaVencimiento",
                table: "Colegios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoBaja",
                table: "Colegios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotasInternas",
                table: "Colegios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrecioMensual",
                table: "Colegios",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HistorialColegios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioRefId = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Detalle = table.Column<string>(type: "text", nullable: false),
                    PorUsuarioId = table.Column<int>(type: "integer", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistorialColegios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MensajesPlataforma",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Cargo = table.Column<string>(type: "text", nullable: true),
                    ColegioNombre = table.Column<string>(type: "text", nullable: true),
                    ColegioRefId = table.Column<int>(type: "integer", nullable: true),
                    Correo = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: true),
                    Mensaje = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    NotaInterna = table.Column<string>(type: "text", nullable: true),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesPlataforma", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistorialColegios");

            migrationBuilder.DropTable(
                name: "MensajesPlataforma");

            migrationBuilder.DropColumn(
                name: "ContactoCorreo",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "ContactoNombre",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "ContactoTelefono",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "FechaBaja",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "FechaRegistro",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "FechaVencimiento",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "MotivoBaja",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "NotasInternas",
                table: "Colegios");

            migrationBuilder.DropColumn(
                name: "PrecioMensual",
                table: "Colegios");
        }
    }
}
