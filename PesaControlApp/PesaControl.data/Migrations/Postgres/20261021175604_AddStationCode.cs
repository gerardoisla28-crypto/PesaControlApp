using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Postgres
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
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Prefix",
                table: "TicketCounters",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StationCode",
                table: "WeighingRecords");

            migrationBuilder.AlterColumn<string>(
                name: "Prefix",
                table: "TicketCounters",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
