using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNuevosCamposCursosMatricula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaMatricula",
                table: "Estudiantes",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "FueCambiadoPorExcepcion",
                table: "Estudiantes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCambioExcepcion",
                table: "Estudiantes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoMatricula",
                table: "Estudiantes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CapacidadMaxima",
                table: "Cursos",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaMatricula",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "FueCambiadoPorExcepcion",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "MotivoCambioExcepcion",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "TipoMatricula",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "CapacidadMaxima",
                table: "Cursos");
        }
    }
}
