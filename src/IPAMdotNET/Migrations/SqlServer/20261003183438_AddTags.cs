using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPAMdotNet.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BackgroundColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    TextColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    ShowTag = table.Column<bool>(type: "bit", nullable: false),
                    Compress = table.Column<bool>(type: "bit", nullable: false),
                    UpdateByScan = table.Column<bool>(type: "bit", nullable: false),
                    Locked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);
            // Étiquettes système de phpIPAM (sans Id explicite : la séquence d'identité avance normalement).
            migrationBuilder.InsertData(
                table: "Tags",
                columns: new[] { "Name", "Description", "BackgroundColor", "TextColor", "ShowTag", "Compress", "UpdateByScan", "Locked" },
                values: new object[,]
                {
                    { "Hors ligne", "Adresse inutilisée ou hors service.", "#f59c99", "#ffffff", true, false, true, true },
                    { "Utilisée", "Adresse attribuée et active.", "#a9c9a4", "#ffffff", true, false, true, true },
                    { "Réservée", "Adresse réservée, à ne pas attribuer.", "#9ac0cd", "#ffffff", true, false, false, true },
                    { "DHCP", "Adresse d'une plage DHCP.", "#c9c9c9", "#ffffff", true, true, false, true },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tags");
        }
    }
}
