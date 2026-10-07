using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class NatLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DestinationAddressId",
                table: "NatRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DestinationSubnetId",
                table: "NatRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeviceId",
                table: "NatRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceAddressId",
                table: "NatRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceSubnetId",
                table: "NatRules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_DestinationAddressId",
                table: "NatRules",
                column: "DestinationAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_DestinationSubnetId",
                table: "NatRules",
                column: "DestinationSubnetId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_DeviceId",
                table: "NatRules",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_SourceAddressId",
                table: "NatRules",
                column: "SourceAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_SourceSubnetId",
                table: "NatRules",
                column: "SourceSubnetId");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_Devices_DeviceId",
                table: "NatRules",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_IpAddresses_DestinationAddressId",
                table: "NatRules",
                column: "DestinationAddressId",
                principalTable: "IpAddresses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_IpAddresses_SourceAddressId",
                table: "NatRules",
                column: "SourceAddressId",
                principalTable: "IpAddresses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_Subnets_DestinationSubnetId",
                table: "NatRules",
                column: "DestinationSubnetId",
                principalTable: "Subnets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_Subnets_SourceSubnetId",
                table: "NatRules",
                column: "SourceSubnetId",
                principalTable: "Subnets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_Devices_DeviceId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_IpAddresses_DestinationAddressId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_IpAddresses_SourceAddressId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_Subnets_DestinationSubnetId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_Subnets_SourceSubnetId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_DestinationAddressId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_DestinationSubnetId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_DeviceId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_SourceAddressId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_SourceSubnetId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "DestinationAddressId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "DestinationSubnetId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "SourceAddressId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "SourceSubnetId",
                table: "NatRules");
        }
    }
}
