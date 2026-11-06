using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Citaciones: profesionales que participan, respuesta de la persona citada (rechazo u otra fecha),
    /// solicitudes de cita del alumno/a o apoderado/a; y quién ve cada nota del calendario.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261105060000_CitacionesRespuestasYVisibilidad")]
    public partial class CitacionesRespuestasYVisibilidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Participantes",
                table: "Citaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Respuesta",
                table: "Citaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaPropuesta",
                table: "Citaciones",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "HoraPropuesta",
                table: "Citaciones",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Destinatario",
                table: "Citaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SolicitadaPorId",
                table: "Citaciones",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisiblePara",
                table: "EventosCalendario",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Participantes", table: "Citaciones");
            migrationBuilder.DropColumn(name: "Respuesta", table: "Citaciones");
            migrationBuilder.DropColumn(name: "FechaPropuesta", table: "Citaciones");
            migrationBuilder.DropColumn(name: "HoraPropuesta", table: "Citaciones");
            migrationBuilder.DropColumn(name: "Destinatario", table: "Citaciones");
            migrationBuilder.DropColumn(name: "SolicitadaPorId", table: "Citaciones");
            migrationBuilder.DropColumn(name: "VisiblePara", table: "EventosCalendario");
        }
    }
}
