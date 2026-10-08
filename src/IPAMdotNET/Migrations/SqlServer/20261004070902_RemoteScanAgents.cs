using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class RemoteScanAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScanAgentId",
                table: "Subnets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemoteAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastContactAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteAgents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subnets_ScanAgentId",
                table: "Subnets",
                column: "ScanAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteAgents_KeyHash",
                table: "RemoteAgents",
                column: "KeyHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Subnets_RemoteAgents_ScanAgentId",
                table: "Subnets",
                column: "ScanAgentId",
                principalTable: "RemoteAgents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subnets_RemoteAgents_ScanAgentId",
                table: "Subnets");

            migrationBuilder.DropTable(
                name: "RemoteAgents");

            migrationBuilder.DropIndex(
                name: "IX_Subnets_ScanAgentId",
                table: "Subnets");

            migrationBuilder.DropColumn(
                name: "ScanAgentId",
                table: "Subnets");
        }
    }
}
