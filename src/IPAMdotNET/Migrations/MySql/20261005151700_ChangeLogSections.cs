using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.MySql
{
    /// <inheritdoc />
    public partial class ChangeLogSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SectionId",
                table: "ChangeLogs",
                type: "int",
                nullable: true);

            // Entrées existantes : section des objets encore présents (ceux supprimés depuis restent réservés aux admins).
            migrationBuilder.Sql("UPDATE `ChangeLogs` SET `SectionId` = `EntityId` WHERE `EntityType` = 'Section'");
            migrationBuilder.Sql("UPDATE `ChangeLogs` SET `SectionId` = (SELECT s.`SectionId` FROM `Subnets` s WHERE s.`Id` = `ChangeLogs`.`EntityId`) WHERE `EntityType` = 'Subnet'");
            migrationBuilder.Sql("UPDATE `ChangeLogs` SET `SectionId` = (SELECT s.`SectionId` FROM `IpAddresses` a JOIN `Subnets` s ON s.`Id` = a.`SubnetId` WHERE a.`Id` = `ChangeLogs`.`EntityId`) WHERE `EntityType` = 'IpAddress'");
            migrationBuilder.Sql("UPDATE `ChangeLogs` SET `SectionId` = (SELECT s.`SectionId` FROM `IpRequests` r JOIN `Subnets` s ON s.`Id` = r.`SubnetId` WHERE r.`Id` = `ChangeLogs`.`EntityId`) WHERE `EntityType` = 'IpRequest'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "ChangeLogs");
        }
    }
}
