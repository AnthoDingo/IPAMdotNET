using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace IPAMdotNet.Migrations.MySql
{
    /// <inheritdoc />
    public partial class NatRuleObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NatRuleObjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    NatRuleId = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    SubnetId = table.Column<int>(type: "int", nullable: true),
                    AddressId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NatRuleObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NatRuleObjects_IpAddresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "IpAddresses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NatRuleObjects_NatRules_NatRuleId",
                        column: x => x.NatRuleId,
                        principalTable: "NatRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NatRuleObjects_Subnets_SubnetId",
                        column: x => x.SubnetId,
                        principalTable: "Subnets",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NatRuleObjects_AddressId",
                table: "NatRuleObjects",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRuleObjects_NatRuleId",
                table: "NatRuleObjects",
                column: "NatRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRuleObjects_SubnetId",
                table: "NatRuleObjects",
                column: "SubnetId");

            // Règles existantes : leur objet unique par côté devient la première ligne de la nouvelle table.
            migrationBuilder.Sql(@"INSERT INTO `NatRuleObjects` (`NatRuleId`, `Side`, `Text`, `SubnetId`, `AddressId`) SELECT `Id`, 0, `Source`, `SourceSubnetId`, `SourceAddressId` FROM `NatRules`");
            migrationBuilder.Sql(@"INSERT INTO `NatRuleObjects` (`NatRuleId`, `Side`, `Text`, `SubnetId`, `AddressId`) SELECT `Id`, 1, `Destination`, `DestinationSubnetId`, `DestinationAddressId` FROM `NatRules`");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_IpAddresses_DestinationAddressId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_IpAddresses_SourceAddressId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_Subnets_DestinationSubnetId",
                table: "NatRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NatRules_Subnets_SourceSubnetId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_DestinationAddressId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_DestinationSubnetId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_SourceAddressId",
                table: "NatRules");

            migrationBuilder.DropIndex(
                name: "IX_NatRules_SourceSubnetId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "Destination",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "DestinationAddressId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "DestinationSubnetId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "SourceAddressId",
                table: "NatRules");

            migrationBuilder.DropColumn(
                name: "SourceSubnetId",
                table: "NatRules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Destination",
                table: "NatRules",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DestinationAddressId",
                table: "NatRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DestinationSubnetId",
                table: "NatRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "NatRules",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SourceAddressId",
                table: "NatRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceSubnetId",
                table: "NatRules",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_DestinationAddressId",
                table: "NatRules",
                column: "DestinationAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_DestinationSubnetId",
                table: "NatRules",
                column: "DestinationSubnetId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_SourceAddressId",
                table: "NatRules",
                column: "SourceAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_NatRules_SourceSubnetId",
                table: "NatRules",
                column: "SourceSubnetId");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_IpAddresses_DestinationAddressId",
                table: "NatRules",
                column: "DestinationAddressId",
                principalTable: "IpAddresses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_IpAddresses_SourceAddressId",
                table: "NatRules",
                column: "SourceAddressId",
                principalTable: "IpAddresses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_Subnets_DestinationSubnetId",
                table: "NatRules",
                column: "DestinationSubnetId",
                principalTable: "Subnets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NatRules_Subnets_SourceSubnetId",
                table: "NatRules",
                column: "SourceSubnetId",
                principalTable: "Subnets",
                principalColumn: "Id");

            // Retour à un seul objet par côté : le premier texte est gardé, sans lien.
            migrationBuilder.Sql(@"UPDATE `NatRules` SET `Source` = COALESCE((SELECT MIN(o.`Text`) FROM `NatRuleObjects` o WHERE o.`NatRuleId` = `NatRules`.`Id` AND o.`Side` = 0), '')");
            migrationBuilder.Sql(@"UPDATE `NatRules` SET `Destination` = COALESCE((SELECT MIN(o.`Text`) FROM `NatRuleObjects` o WHERE o.`NatRuleId` = `NatRules`.`Id` AND o.`Side` = 1), '')");

            migrationBuilder.DropTable(
                name: "NatRuleObjects");
        }
    }
}
