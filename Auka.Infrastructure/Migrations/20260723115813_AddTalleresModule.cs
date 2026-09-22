using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTalleresModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TallerId",
                table: "Estudiantes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Talleres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Horario = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocenteCargoId = table.Column<int>(type: "int", nullable: false),
                    ColegioId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Talleres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Talleres_Colegios_ColegioId",
                        column: x => x.ColegioId,
                        principalTable: "Colegios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Talleres_Usuarios_DocenteCargoId",
                        column: x => x.DocenteCargoId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Estudiantes_TallerId",
                table: "Estudiantes",
                column: "TallerId");

            migrationBuilder.CreateIndex(
                name: "IX_Talleres_ColegioId",
                table: "Talleres",
                column: "ColegioId");

            migrationBuilder.CreateIndex(
                name: "IX_Talleres_DocenteCargoId",
                table: "Talleres",
                column: "DocenteCargoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Estudiantes_Talleres_TallerId",
                table: "Estudiantes",
                column: "TallerId",
                principalTable: "Talleres",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Estudiantes_Talleres_TallerId",
                table: "Estudiantes");

            migrationBuilder.DropTable(
                name: "Talleres");

            migrationBuilder.DropIndex(
                name: "IX_Estudiantes_TallerId",
                table: "Estudiantes");

            migrationBuilder.DropColumn(
                name: "TallerId",
                table: "Estudiantes");
        }
    }
}
