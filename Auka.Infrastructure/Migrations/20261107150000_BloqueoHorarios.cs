using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Bloqueo de horarios (docente confirma + UTP aprueba), desacuerdo del docente y solicitudes de cambio entre docentes.
    /// Solo agrega columnas (con valor por defecto "sin bloquear") y una tabla nueva: no cambia datos existentes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107150000_BloqueoHorarios")]
    public partial class BloqueoHorarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "HorarioAprobadoUtc", table: "Usuarios", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "HorarioBloqueado", table: "Usuarios", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "HorarioObservacion", table: "Usuarios", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "HorarioBloqueado", table: "Cursos", type: "boolean", nullable: false, defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SolicitudesCambioHorario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    CursoId = table.Column<int>(type: "integer", nullable: false),
                    ClaseAId = table.Column<int>(type: "integer", nullable: false),
                    DiaDestino = table.Column<int>(type: "integer", nullable: false),
                    BloqueDestinoId = table.Column<int>(type: "integer", nullable: false),
                    ClaseBId = table.Column<int>(type: "integer", nullable: true),
                    ProfesorConsultadoId = table.Column<int>(type: "integer", nullable: false),
                    SolicitanteId = table.Column<int>(type: "integer", nullable: false),
                    Resumen = table.Column<string>(type: "text", nullable: false),
                    MotivoMostrado = table.Column<string>(type: "text", nullable: true),
                    MotivoInterno = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Respuesta = table.Column<string>(type: "text", nullable: true),
                    CreadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesCambioHorario", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_SolicitudesCambioHorario_ColegioId_Estado", table: "SolicitudesCambioHorario", columns: new[] { "ColegioId", "Estado" });
            migrationBuilder.CreateIndex(name: "IX_SolicitudesCambioHorario_ProfesorConsultadoId", table: "SolicitudesCambioHorario", column: "ProfesorConsultadoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SolicitudesCambioHorario");
            migrationBuilder.DropColumn(name: "HorarioAprobadoUtc", table: "Usuarios");
            migrationBuilder.DropColumn(name: "HorarioBloqueado", table: "Usuarios");
            migrationBuilder.DropColumn(name: "HorarioObservacion", table: "Usuarios");
            migrationBuilder.DropColumn(name: "HorarioBloqueado", table: "Cursos");
        }
    }
}
