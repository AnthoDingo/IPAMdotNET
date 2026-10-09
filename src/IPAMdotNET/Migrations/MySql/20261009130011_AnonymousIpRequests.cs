using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AnonymousIpRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RequestedById",
                table: "IpRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedAddress",
                table: "IpRequests",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Count",
                table: "IpRequests",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RequesterEmail",
                table: "IpRequests",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Count",
                table: "IpRequests");

            migrationBuilder.DropColumn(
                name: "RequesterEmail",
                table: "IpRequests");

            migrationBuilder.AlterColumn<int>(
                name: "RequestedById",
                table: "IpRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedAddress",
                table: "IpRequests",
                type: "varchar(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
