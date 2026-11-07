using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Plan Premium — administrador del colegio:
    ///  - Permisos por acción (crear clubes, inscribir, horarios, profesores jefes) y resumen psicosocial opcional del administrador.
    ///  - Matrícula: especialidad TP y datos de traslado (colegio de origen, RBD, documentos pendientes).
    ///  - Clubes: categoría, cupo e inscripciones (un alumno puede estar en varios clubes).
    ///    Las inscripciones antiguas (Estudiantes.TallerId) se copian a la tabla nueva antes de quitar esa columna.
    /// No borra datos de alumnos ni de clubes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107030000_AdministradorPremium")]
    public partial class AdministradorPremium : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Colegios ----
            migrationBuilder.AddColumn<string>(name: "Permisos", table: "Colegios", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "PsicosocialAdministrador", table: "Colegios", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "Especialidades", table: "Colegios", type: "text", nullable: true);

            // ---- Estudiantes: especialidad y traslado ----
            migrationBuilder.AddColumn<string>(name: "Especialidad", table: "Estudiantes", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ColegioOrigen", table: "Estudiantes", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "RbdOrigen", table: "Estudiantes", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ComunaOrigen", table: "Estudiantes", type: "text", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "FechaTraslado", table: "Estudiantes", type: "date", nullable: true);
            migrationBuilder.AddColumn<string>(name: "MotivoTraslado", table: "Estudiantes", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "NecesidadesEducativas", table: "Estudiantes", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "DocumentosPendientes", table: "Estudiantes", type: "text", nullable: true);

            // ---- Talleres: categoría y cupo ----
            migrationBuilder.AddColumn<string>(name: "Categoria", table: "Talleres", type: "text", nullable: false, defaultValue: "Deportivo");
            migrationBuilder.AddColumn<int>(name: "Cupo", table: "Talleres", type: "integer", nullable: true);

            // ---- Inscripciones a clubes ----
            migrationBuilder.CreateTable(
                name: "InscripcionesTaller",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    TallerId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InscritoPorId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InscripcionesTaller", x => x.Id);
                    table.ForeignKey(name: "FK_InscripcionesTaller_Estudiantes_EstudianteId", column: x => x.EstudianteId,
                        principalTable: "Estudiantes", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_InscripcionesTaller_Talleres_TallerId", column: x => x.TallerId,
                        principalTable: "Talleres", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_InscripcionesTaller_EstudianteId", table: "InscripcionesTaller", column: "EstudianteId");
            migrationBuilder.CreateIndex(name: "IX_InscripcionesTaller_TallerId_EstudianteId", table: "InscripcionesTaller",
                columns: new[] { "TallerId", "EstudianteId" }, unique: true);

            // Copia las inscripciones antiguas (un club por alumno) a la tabla nueva
            migrationBuilder.Sql(@"
                INSERT INTO ""InscripcionesTaller"" (""ColegioId"", ""TallerId"", ""EstudianteId"", ""FechaUtc"")
                SELECT e.""ColegioId"", e.""TallerId"", e.""Id"", NOW()
                FROM ""Estudiantes"" e
                WHERE e.""TallerId"" IS NOT NULL
                  AND EXISTS (SELECT 1 FROM ""Talleres"" t WHERE t.""Id"" = e.""TallerId"");");

            migrationBuilder.Sql(@"ALTER TABLE ""Estudiantes"" DROP CONSTRAINT IF EXISTS ""FK_Estudiantes_Talleres_TallerId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Estudiantes_TallerId"";");
            migrationBuilder.DropColumn(name: "TallerId", table: "Estudiantes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "TallerId", table: "Estudiantes", type: "integer", nullable: true);
            migrationBuilder.Sql(@"
                UPDATE ""Estudiantes"" e SET ""TallerId"" = i.""TallerId""
                FROM (SELECT DISTINCT ON (""EstudianteId"") ""EstudianteId"", ""TallerId"" FROM ""InscripcionesTaller"" ORDER BY ""EstudianteId"", ""FechaUtc"") i
                WHERE i.""EstudianteId"" = e.""Id"";");
            migrationBuilder.CreateIndex(name: "IX_Estudiantes_TallerId", table: "Estudiantes", column: "TallerId");
            migrationBuilder.AddForeignKey(name: "FK_Estudiantes_Talleres_TallerId", table: "Estudiantes", column: "TallerId",
                principalTable: "Talleres", principalColumn: "Id");

            migrationBuilder.DropTable(name: "InscripcionesTaller");
            migrationBuilder.DropColumn(name: "Categoria", table: "Talleres");
            migrationBuilder.DropColumn(name: "Cupo", table: "Talleres");
            foreach (var c in new[] { "Especialidad", "ColegioOrigen", "RbdOrigen", "ComunaOrigen", "FechaTraslado", "MotivoTraslado", "NecesidadesEducativas", "DocumentosPendientes" })
                migrationBuilder.DropColumn(name: c, table: "Estudiantes");
            migrationBuilder.DropColumn(name: "Permisos", table: "Colegios");
            migrationBuilder.DropColumn(name: "PsicosocialAdministrador", table: "Colegios");
            migrationBuilder.DropColumn(name: "Especialidades", table: "Colegios");
        }
    }
}
