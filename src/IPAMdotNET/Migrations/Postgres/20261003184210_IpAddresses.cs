using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IPAMdotNet.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class IpAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemKey",
                table: "Tags",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
            // Clés stables des étiquettes système, pour l'agent de scan (le nom reste modifiable).
            migrationBuilder.UpdateData(table: "Tags", keyColumn: "Name", keyValue: "Hors ligne", column: "SystemKey", value: "offline");
            migrationBuilder.UpdateData(table: "Tags", keyColumn: "Name", keyValue: "Utilisée", column: "SystemKey", value: "used");
            migrationBuilder.UpdateData(table: "Tags", keyColumn: "Name", keyValue: "Réservée", column: "SystemKey", value: "reserved");
            migrationBuilder.UpdateData(table: "Tags", keyColumn: "Name", keyValue: "DHCP", column: "SystemKey", value: "dhcp");

            migrationBuilder.AddColumn<bool>(
                name: "Discover",
                table: "Subnets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastScanAt",
                table: "Subnets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PingCheck",
                table: "Subnets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "IpAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubnetId = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MacAddress = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    Owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TagId = table.Column<int>(type: "integer", nullable: true),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    ExcludePing = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IpAddresses_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IpAddresses_Subnets_SubnetId",
                        column: x => x.SubnetId,
                        principalTable: "Subnets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IpAddresses_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_DeviceId",
                table: "IpAddresses",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_SubnetId_Address",
                table: "IpAddresses",
                columns: new[] { "SubnetId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_TagId",
                table: "IpAddresses",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IpAddresses");

            migrationBuilder.DropColumn(
                name: "SystemKey",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "Discover",
                table: "Subnets");

            migrationBuilder.DropColumn(
                name: "LastScanAt",
                table: "Subnets");

            migrationBuilder.DropColumn(
                name: "PingCheck",
                table: "Subnets");
        }
    }
}
