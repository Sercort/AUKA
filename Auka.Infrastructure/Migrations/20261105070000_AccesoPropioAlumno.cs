using Auka.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <summary>
    /// Alumnos/as sin cuenta propia (kínder a 6° básico: todo lo maneja su apoderado/a)
    /// y citaciones que el equipo decide informar también a la otra parte (alumno/a o apoderado/a).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261105070000_AccesoPropioAlumno")]
    public partial class AccesoPropioAlumno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AccesoPropio",
                table: "Estudiantes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "InformarOtro",
                table: "Citaciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Los alumnos/as que hoy están de kínder a 6° básico quedan sin cuenta propia
            migrationBuilder.Sql(@"UPDATE ""Estudiantes"" SET ""AccesoPropio"" = false
WHERE ""CursoId"" IN (SELECT ""Id"" FROM ""Cursos"" WHERE ""Nombre"" ~* '^\s*(pre[\s-]*k|k[ií]nder|kinder|nt\s*[12]|transici[oó]n|([1-6]\s*(°|º|o|ro|do|to|er)?|primero?|segundo|tercero?|cuarto|quinto|sexto)\s*(a[ñn]o\s*)?b[aá]sico)');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AccesoPropio", table: "Estudiantes");
            migrationBuilder.DropColumn(name: "InformarOtro", table: "Citaciones");
        }
    }
}
