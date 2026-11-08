using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Asistencia por bloque: estado (presente, ausente, atrasado, justificado), hora de llegada, curso, bloque y quién la registró.
    /// También: clase finalizada (solo la UTP corrige después), visibilidad del % del curso para colegas
    /// y atrasos registrados por inspectoría en la entrada.
    /// Solo agrega columnas y una tabla: lo que ya existía queda como "presente" o "ausente" según su valor.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108050000_AsistenciaPorBloque")]
    public partial class AsistenciaPorBloque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "Estado", table: "Asistencias", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<TimeSpan>(name: "HoraLlegada", table: "Asistencias", type: "interval", nullable: true);
            migrationBuilder.AddColumn<int>(name: "CursoId", table: "Asistencias", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "BloqueJornadaId", table: "Asistencias", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "RegistradoPorId", table: "Asistencias", type: "integer", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Observacion", table: "Asistencias", type: "text", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "CerradaUtc", table: "Asistencias", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "AsistenciaVisibleColegas", table: "Cursos", type: "boolean", nullable: false, defaultValue: true);

            // Atrasos que registra inspectoría en la entrada
            migrationBuilder.CreateTable(
                name: "AtrasosLlegada",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Hora = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    Justificado = table.Column<bool>(type: "boolean", nullable: false),
                    RegistradoPorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtrasosLlegada", x => x.Id);
                    table.ForeignKey(name: "FK_AtrasosLlegada_Estudiantes_EstudianteId", column: x => x.EstudianteId, principalTable: "Estudiantes", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_AtrasosLlegada_ColegioId_Fecha", table: "AtrasosLlegada", columns: new[] { "ColegioId", "Fecha" });
            migrationBuilder.CreateIndex(name: "IX_AtrasosLlegada_EstudianteId", table: "AtrasosLlegada", column: "EstudianteId");
            // Lo que ya estaba marcado como ausente queda con estado Ausente
            migrationBuilder.Sql("UPDATE \"Asistencias\" SET \"Estado\" = 1 WHERE \"Presente\" = false;");
            migrationBuilder.CreateIndex(name: "IX_Asistencias_CursoId_Fecha", table: "Asistencias", columns: new[] { "CursoId", "Fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AtrasosLlegada");
            migrationBuilder.DropColumn(name: "CerradaUtc", table: "Asistencias");
            migrationBuilder.DropColumn(name: "AsistenciaVisibleColegas", table: "Cursos");
            migrationBuilder.DropIndex(name: "IX_Asistencias_CursoId_Fecha", table: "Asistencias");
            migrationBuilder.DropColumn(name: "Estado", table: "Asistencias");
            migrationBuilder.DropColumn(name: "HoraLlegada", table: "Asistencias");
            migrationBuilder.DropColumn(name: "CursoId", table: "Asistencias");
            migrationBuilder.DropColumn(name: "BloqueJornadaId", table: "Asistencias");
            migrationBuilder.DropColumn(name: "RegistradoPorId", table: "Asistencias");
            migrationBuilder.DropColumn(name: "Observacion", table: "Asistencias");
        }
    }
}
