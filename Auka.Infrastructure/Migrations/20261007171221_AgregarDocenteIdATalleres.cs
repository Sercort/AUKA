using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarDocenteIdATalleres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocenteId",
                table: "Talleres",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Talleres_DocenteId",
                table: "Talleres",
                column: "DocenteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Talleres_Usuarios_DocenteId",
                table: "Talleres",
                column: "DocenteId",
                principalTable: "Usuarios",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Talleres_Usuarios_DocenteId",
                table: "Talleres");

            migrationBuilder.DropIndex(
                name: "IX_Talleres_DocenteId",
                table: "Talleres");

            migrationBuilder.DropColumn(
                name: "DocenteId",
                table: "Talleres");
        }
    }
}
