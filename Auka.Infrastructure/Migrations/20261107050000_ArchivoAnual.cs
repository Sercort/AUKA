using System;
using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Archivo de años escolares: guarda en qué curso estuvo cada alumno/a cada año (MatriculasAnuales).
    /// Se llena con la matrícula actual de cada alumno/a. No modifica datos existentes.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261107050000_ArchivoAnual")]
    public partial class ArchivoAnual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatriculasAnuales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ColegioId = table.Column<int>(type: "integer", nullable: false),
                    EstudianteId = table.Column<int>(type: "integer", nullable: false),
                    CursoId = table.Column<int>(type: "integer", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatriculasAnuales", x => x.Id);
                    table.ForeignKey(name: "FK_MatriculasAnuales_Cursos_CursoId", column: x => x.CursoId,
                        principalTable: "Cursos", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_MatriculasAnuales_Estudiantes_EstudianteId", column: x => x.EstudianteId,
                        principalTable: "Estudiantes", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_MatriculasAnuales_CursoId", table: "MatriculasAnuales", column: "CursoId");
            migrationBuilder.CreateIndex(name: "IX_MatriculasAnuales_EstudianteId_Anio", table: "MatriculasAnuales",
                columns: new[] { "EstudianteId", "Anio" }, unique: true);

            // Punto de partida: el curso actual de cada alumno/a queda registrado en su año
            migrationBuilder.Sql(@"
                INSERT INTO ""MatriculasAnuales"" (""ColegioId"", ""EstudianteId"", ""CursoId"", ""Anio"", ""Tipo"", ""FechaUtc"")
                SELECT e.""ColegioId"", e.""Id"", c.""Id"", c.""AnioAcademico"", e.""TipoMatricula"", NOW()
                FROM ""Estudiantes"" e
                JOIN ""Cursos"" c ON c.""Id"" = e.""CursoId""
                ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MatriculasAnuales");
        }
    }
}
