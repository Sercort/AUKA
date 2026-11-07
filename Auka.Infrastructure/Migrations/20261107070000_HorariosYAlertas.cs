using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Horarios: jornada por colegio (bloques, recreos, almuerzo, salida del viernes), clases por curso y departamento de cada docente.
    /// Alertas: límites de alerta de convivencia por colegio. No modifica datos existentes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107070000_HorariosYAlertas")]
    public partial class HorariosYAlertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(name: "ViernesSalida", table: "Colegios", type: "interval", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "ViernesTardeActiva", table: "Colegios", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "ConfigAlertas", table: "Colegios", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Departamento", table: "Usuarios", type: "text", nullable: true);

            migrationBuilder.CreateTable(
                name: "BloquesJornada",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraFin = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_BloquesJornada", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_BloquesJornada_ColegioId_Orden", table: "BloquesJornada", columns: new[] { "ColegioId", "Orden" }, unique: true);

            migrationBuilder.CreateTable(
                name: "ClasesHorario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    CursoId = table.Column<int>(type: "integer", nullable: false),
                    Dia = table.Column<int>(type: "integer", nullable: false),
                    BloqueJornadaId = table.Column<int>(type: "integer", nullable: false),
                    Asignatura = table.Column<string>(type: "text", nullable: false),
                    ProfesorId = table.Column<int>(type: "integer", nullable: true),
                    Sala = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClasesHorario", x => x.Id);
                    table.ForeignKey(name: "FK_ClasesHorario_BloquesJornada_BloqueJornadaId", column: x => x.BloqueJornadaId,
                        principalTable: "BloquesJornada", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_ClasesHorario_Cursos_CursoId", column: x => x.CursoId,
                        principalTable: "Cursos", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_ClasesHorario_Usuarios_ProfesorId", column: x => x.ProfesorId,
                        principalTable: "Usuarios", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
                });
            migrationBuilder.CreateIndex(name: "IX_ClasesHorario_BloqueJornadaId", table: "ClasesHorario", column: "BloqueJornadaId");
            migrationBuilder.CreateIndex(name: "IX_ClasesHorario_CursoId_Dia_BloqueJornadaId", table: "ClasesHorario", columns: new[] { "CursoId", "Dia", "BloqueJornadaId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_ClasesHorario_ProfesorId_Dia_BloqueJornadaId", table: "ClasesHorario", columns: new[] { "ProfesorId", "Dia", "BloqueJornadaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ClasesHorario");
            migrationBuilder.DropTable(name: "BloquesJornada");
            migrationBuilder.DropColumn(name: "ViernesSalida", table: "Colegios");
            migrationBuilder.DropColumn(name: "ViernesTardeActiva", table: "Colegios");
            migrationBuilder.DropColumn(name: "ConfigAlertas", table: "Colegios");
            migrationBuilder.DropColumn(name: "Departamento", table: "Usuarios");
        }
    }
}
