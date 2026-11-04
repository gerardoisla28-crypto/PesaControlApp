using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddExportPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExportPending",
                table: "WeighingRecords",
                type: "INTEGER",
                // Los pesajes que ya existen quedan pendientes de exportar.
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportPending",
                table: "WeighingRecords");
        }
    }
}
