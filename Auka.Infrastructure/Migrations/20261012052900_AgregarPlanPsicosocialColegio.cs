using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPlanPsicosocialColegio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PlanPsicosocialActivo",
                table: "Colegios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Activa el plan Premium en los colegios existentes (entorno de pruebas)
            migrationBuilder.Sql("UPDATE \"Colegios\" SET \"PlanPsicosocialActivo\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanPsicosocialActivo",
                table: "Colegios");
        }
    }
}
