using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IPAMdotNet.Migrations.Postgres
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
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemoteAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastContactAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
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
