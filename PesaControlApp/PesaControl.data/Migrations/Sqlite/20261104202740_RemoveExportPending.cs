using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Sqlite
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
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
