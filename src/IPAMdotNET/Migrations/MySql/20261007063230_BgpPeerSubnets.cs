using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace IPAMdotNet.Migrations.MySql
{
    /// <inheritdoc />
    public partial class BgpPeerSubnets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BgpPeerSubnets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BgpPeerId = table.Column<int>(type: "int", nullable: false),
                    SubnetId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BgpPeerSubnets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BgpPeerSubnets_BgpPeers_BgpPeerId",
                        column: x => x.BgpPeerId,
                        principalTable: "BgpPeers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BgpPeerSubnets_Subnets_SubnetId",
                        column: x => x.SubnetId,
                        principalTable: "Subnets",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BgpPeerSubnets_BgpPeerId_SubnetId_Direction",
                table: "BgpPeerSubnets",
                columns: new[] { "BgpPeerId", "SubnetId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BgpPeerSubnets_SubnetId",
                table: "BgpPeerSubnets",
                column: "SubnetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BgpPeerSubnets");
        }
    }
}
