using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddStationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StationCode",
                table: "WeighingRecords",
                type: "TEXT",
                maxLength: 4,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StationCode",
                table: "WeighingRecords");
        }
    }
}
