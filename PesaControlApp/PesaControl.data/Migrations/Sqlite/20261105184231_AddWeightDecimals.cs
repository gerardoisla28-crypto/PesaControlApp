using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddWeightDecimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WeightDecimals",
                table: "WeighingRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightDecimals",
                table: "ScaleConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightDecimals",
                table: "WeighingRecords");

            migrationBuilder.DropColumn(
                name: "WeightDecimals",
                table: "ScaleConfigs");
        }
    }
}
