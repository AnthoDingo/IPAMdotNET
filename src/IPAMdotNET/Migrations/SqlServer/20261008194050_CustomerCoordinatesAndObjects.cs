using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class CustomerCoordinatesAndObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "Vlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "IpAddresses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Latitude",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Longitude",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vlans_CustomerId",
                table: "Vlans",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_CustomerId",
                table: "IpAddresses",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_IpAddresses_Customers_CustomerId",
                table: "IpAddresses",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Vlans_Customers_CustomerId",
                table: "Vlans",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IpAddresses_Customers_CustomerId",
                table: "IpAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_Vlans_Customers_CustomerId",
                table: "Vlans");

            migrationBuilder.DropIndex(
                name: "IX_Vlans_CustomerId",
                table: "Vlans");

            migrationBuilder.DropIndex(
                name: "IX_IpAddresses_CustomerId",
                table: "IpAddresses");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Vlans");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "IpAddresses");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Customers");
        }
    }
}
