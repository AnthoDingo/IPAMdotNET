using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddVlanDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vlans_Number",
                table: "Vlans");

            migrationBuilder.AddColumn<int>(
                name: "DomainId",
                table: "Vlans",
                type: "int",
                nullable: false,
                // Les VLAN existants vont dans le domaine « default » inséré ci-dessous (premier identifiant d'une table neuve).
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "VlanDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VlanDomains", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "VlanDomains",
                columns: new[] { "Name", "Description" },
                values: new object[] { "default", "Domaine L2 par défaut" });

            migrationBuilder.CreateIndex(
                name: "IX_Vlans_DomainId_Number",
                table: "Vlans",
                columns: new[] { "DomainId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VlanDomains_Name",
                table: "VlanDomains",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Vlans_VlanDomains_DomainId",
                table: "Vlans",
                column: "DomainId",
                principalTable: "VlanDomains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vlans_VlanDomains_DomainId",
                table: "Vlans");

            migrationBuilder.DropTable(
                name: "VlanDomains");

            migrationBuilder.DropIndex(
                name: "IX_Vlans_DomainId_Number",
                table: "Vlans");

            migrationBuilder.DropColumn(
                name: "DomainId",
                table: "Vlans");

            migrationBuilder.CreateIndex(
                name: "IX_Vlans_Number",
                table: "Vlans",
                column: "Number",
                unique: true);
        }
    }
}
