using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduNexus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuloHorariosClases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoraFin",
                table: "HorariosClases");

            migrationBuilder.DropColumn(
                name: "HoraInicio",
                table: "HorariosClases");

            migrationBuilder.RenameColumn(
                name: "DiaSemana",
                table: "HorariosClases",
                newName: "ProfesorId");

            migrationBuilder.AddColumn<int>(
                name: "BloqueHorarioId",
                table: "HorariosClases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CursoId",
                table: "HorariosClases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Codigo",
                table: "Asignaturas",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BloquesHorarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiaSemana = table.Column<int>(type: "int", nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "time", nullable: false),
                    HoraFin = table.Column<TimeSpan>(type: "time", nullable: false),
                    NumeroBloque = table.Column<int>(type: "int", nullable: false),
                    EsRecreo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloquesHorarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HorariosClases_BloqueHorarioId",
                table: "HorariosClases",
                column: "BloqueHorarioId");

            migrationBuilder.CreateIndex(
                name: "IX_HorariosClases_CursoId",
                table: "HorariosClases",
                column: "CursoId");

            migrationBuilder.CreateIndex(
                name: "IX_HorariosClases_ProfesorId",
                table: "HorariosClases",
                column: "ProfesorId");

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_BloquesHorarios_BloqueHorarioId",
                table: "HorariosClases",
                column: "BloqueHorarioId",
                principalTable: "BloquesHorarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_Cursos_CursoId",
                table: "HorariosClases",
                column: "CursoId",
                principalTable: "Cursos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HorariosClases_Usuarios_ProfesorId",
                table: "HorariosClases",
                column: "ProfesorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_BloquesHorarios_BloqueHorarioId",
                table: "HorariosClases");

            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_Cursos_CursoId",
                table: "HorariosClases");

            migrationBuilder.DropForeignKey(
                name: "FK_HorariosClases_Usuarios_ProfesorId",
                table: "HorariosClases");

            migrationBuilder.DropTable(
                name: "BloquesHorarios");

            migrationBuilder.DropIndex(
                name: "IX_HorariosClases_BloqueHorarioId",
                table: "HorariosClases");

            migrationBuilder.DropIndex(
                name: "IX_HorariosClases_CursoId",
                table: "HorariosClases");

            migrationBuilder.DropIndex(
                name: "IX_HorariosClases_ProfesorId",
                table: "HorariosClases");

            migrationBuilder.DropColumn(
                name: "BloqueHorarioId",
                table: "HorariosClases");

            migrationBuilder.DropColumn(
                name: "CursoId",
                table: "HorariosClases");

            migrationBuilder.DropColumn(
                name: "Codigo",
                table: "Asignaturas");

            migrationBuilder.RenameColumn(
                name: "ProfesorId",
                table: "HorariosClases",
                newName: "DiaSemana");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HoraFin",
                table: "HorariosClases",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HoraInicio",
                table: "HorariosClases",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }
    }
}
