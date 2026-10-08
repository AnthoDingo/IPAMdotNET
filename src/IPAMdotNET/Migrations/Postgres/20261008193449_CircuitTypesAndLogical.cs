using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IPAMdotNet.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class CircuitTypesAndLogical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeviceAId",
                table: "Circuits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeviceBId",
                table: "Circuits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TypeId",
                table: "Circuits",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CircuitTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CircuitTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogicalCircuits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Cid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogicalCircuits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogicalCircuitMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LogicalCircuitId = table.Column<int>(type: "integer", nullable: false),
                    CircuitId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogicalCircuitMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogicalCircuitMembers_Circuits_CircuitId",
                        column: x => x.CircuitId,
                        principalTable: "Circuits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LogicalCircuitMembers_LogicalCircuits_LogicalCircuitId",
                        column: x => x.LogicalCircuitId,
                        principalTable: "LogicalCircuits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Types saisis en texte libre jusqu'ici : un type paramétrable par valeur distincte, puis rattachement des circuits.
            migrationBuilder.Sql("INSERT INTO \"CircuitTypes\" (\"Name\", \"Color\") SELECT DISTINCT btrim(\"Type\"), '#6c757d' FROM \"Circuits\" WHERE \"Type\" IS NOT NULL AND btrim(\"Type\") <> ''");
            migrationBuilder.Sql("UPDATE \"Circuits\" c SET \"TypeId\" = t.\"Id\" FROM \"CircuitTypes\" t WHERE t.\"Name\" = btrim(c.\"Type\")");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Circuits");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_DeviceAId",
                table: "Circuits",
                column: "DeviceAId");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_DeviceBId",
                table: "Circuits",
                column: "DeviceBId");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_TypeId",
                table: "Circuits",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CircuitTypes_Name",
                table: "CircuitTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogicalCircuitMembers_CircuitId",
                table: "LogicalCircuitMembers",
                column: "CircuitId");

            migrationBuilder.CreateIndex(
                name: "IX_LogicalCircuitMembers_LogicalCircuitId_CircuitId",
                table: "LogicalCircuitMembers",
                columns: new[] { "LogicalCircuitId", "CircuitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogicalCircuits_Cid",
                table: "LogicalCircuits",
                column: "Cid",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Circuits_CircuitTypes_TypeId",
                table: "Circuits",
                column: "TypeId",
                principalTable: "CircuitTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Circuits_Devices_DeviceAId",
                table: "Circuits",
                column: "DeviceAId",
                principalTable: "Devices",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Circuits_Devices_DeviceBId",
                table: "Circuits",
                column: "DeviceBId",
                principalTable: "Devices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Circuits_CircuitTypes_TypeId",
                table: "Circuits");

            migrationBuilder.DropForeignKey(
                name: "FK_Circuits_Devices_DeviceAId",
                table: "Circuits");

            migrationBuilder.DropForeignKey(
                name: "FK_Circuits_Devices_DeviceBId",
                table: "Circuits");

            migrationBuilder.DropTable(
                name: "CircuitTypes");

            migrationBuilder.DropTable(
                name: "LogicalCircuitMembers");

            migrationBuilder.DropTable(
                name: "LogicalCircuits");

            migrationBuilder.DropIndex(
                name: "IX_Circuits_DeviceAId",
                table: "Circuits");

            migrationBuilder.DropIndex(
                name: "IX_Circuits_DeviceBId",
                table: "Circuits");

            migrationBuilder.DropIndex(
                name: "IX_Circuits_TypeId",
                table: "Circuits");

            migrationBuilder.DropColumn(
                name: "DeviceAId",
                table: "Circuits");

            migrationBuilder.DropColumn(
                name: "DeviceBId",
                table: "Circuits");

            migrationBuilder.DropColumn(
                name: "TypeId",
                table: "Circuits");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Circuits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
