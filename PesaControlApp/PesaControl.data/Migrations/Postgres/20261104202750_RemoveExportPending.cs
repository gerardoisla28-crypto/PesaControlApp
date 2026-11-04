using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RemoveExportPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportPending",
                table: "WeighingRecords");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExportPending",
                table: "WeighingRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
