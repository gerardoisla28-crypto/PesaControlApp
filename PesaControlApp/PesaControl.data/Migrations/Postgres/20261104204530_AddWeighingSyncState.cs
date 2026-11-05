using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PesaControl.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddWeighingSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "WeighingRecords",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "SyncStatus",
                table: "WeighingRecords",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedAt",
                table: "WeighingRecords",
                type: "timestamp without time zone",
                nullable: true);

            // Los pesajes anteriores parten con la hora de su último movimiento y quedan pendientes de sincronizar.
            migrationBuilder.Sql("""UPDATE "WeighingRecords" SET "ModifiedAt" = COALESCE("WeighOutTime", "WeighInTime");""");

            migrationBuilder.CreateIndex(
                name: "IX_WeighingRecords_SyncStatus",
                table: "WeighingRecords",
                column: "SyncStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeighingRecords_SyncStatus",
                table: "WeighingRecords");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "WeighingRecords");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                table: "WeighingRecords");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "WeighingRecords");
        }
    }
}
