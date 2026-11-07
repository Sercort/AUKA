using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Horario por curso: viernes propio de cada curso, horas de contrato y confirmación del horario de cada docente,
    /// y fecha de última modificación de cada clase. No modifica datos existentes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107090000_HorarioPorCurso")]
    public partial class HorarioPorCurso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(name: "ViernesSalida", table: "Cursos", type: "interval", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "ViernesTarde", table: "Cursos", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<int>(name: "HorasContrato", table: "Usuarios", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "HorarioConfirmadoUtc", table: "Usuarios", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ModificadaUtc", table: "ClasesHorario", type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ViernesSalida", table: "Cursos");
            migrationBuilder.DropColumn(name: "ViernesTarde", table: "Cursos");
            migrationBuilder.DropColumn(name: "HorasContrato", table: "Usuarios");
            migrationBuilder.DropColumn(name: "HorarioConfirmadoUtc", table: "Usuarios");
            migrationBuilder.DropColumn(name: "ModificadaUtc", table: "ClasesHorario");
        }
    }
}
