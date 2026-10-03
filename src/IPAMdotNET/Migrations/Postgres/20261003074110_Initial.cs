using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IPAMdotNet.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Nameservers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Servers = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nameservers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NatRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourcePort = table.Column<int>(type: "integer", nullable: true),
                    Destination = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DestinationPort = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NatRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vrfs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RouteDistinguisher = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vrfs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BgpPeers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LocalAs = table.Column<long>(type: "bigint", nullable: false),
                    LocalAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    PeerAs = table.Column<long>(type: "bigint", nullable: false),
                    PeerAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    VrfId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BgpPeers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BgpPeers_Vrfs_VrfId",
                        column: x => x.VrfId,
                        principalTable: "Vrfs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Subnets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SectionId = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false),
                    PrefixLength = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VlanId = table.Column<int>(type: "integer", nullable: true),
                    VrfId = table.Column<int>(type: "integer", nullable: true),
                    NameserverId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subnets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subnets_Nameservers_NameserverId",
                        column: x => x.NameserverId,
                        principalTable: "Nameservers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Subnets_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subnets_Vlans_VlanId",
                        column: x => x.VlanId,
                        principalTable: "Vlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Subnets_Vrfs_VrfId",
                        column: x => x.VrfId,
                        principalTable: "Vrfs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BgpPeers_VrfId",
                table: "BgpPeers",
                column: "VrfId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Name",
                table: "Sections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subnets_NameserverId",
                table: "Subnets",
                column: "NameserverId");

            migrationBuilder.CreateIndex(
                name: "IX_Subnets_SectionId_Address_PrefixLength",
                table: "Subnets",
                columns: new[] { "SectionId", "Address", "PrefixLength" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subnets_VlanId",
                table: "Subnets",
                column: "VlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Subnets_VrfId",
                table: "Subnets",
                column: "VrfId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vlans_Number",
                table: "Vlans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vrfs_Name",
                table: "Vrfs",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BgpPeers");

            migrationBuilder.DropTable(
                name: "NatRules");

            migrationBuilder.DropTable(
                name: "Subnets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Nameservers");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Vlans");

            migrationBuilder.DropTable(
                name: "Vrfs");
        }
    }
}
