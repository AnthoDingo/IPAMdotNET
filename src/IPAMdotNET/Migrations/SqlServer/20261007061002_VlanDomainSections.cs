using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class VlanDomainSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VlanDomainSections",
                columns: table => new
                {
                    SectionsId = table.Column<int>(type: "int", nullable: false),
                    VlanDomainId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VlanDomainSections", x => new { x.SectionsId, x.VlanDomainId });
                    table.ForeignKey(
                        name: "FK_VlanDomainSections_Sections_SectionsId",
                        column: x => x.SectionsId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VlanDomainSections_VlanDomains_VlanDomainId",
                        column: x => x.VlanDomainId,
                        principalTable: "VlanDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VlanDomainSections_VlanDomainId",
                table: "VlanDomainSections",
                column: "VlanDomainId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VlanDomainSections");
        }
    }
}
