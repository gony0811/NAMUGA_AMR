using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationTagMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationTag = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TaskIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    JobIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationTagMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationTagMappings_LocationTag",
                table: "LocationTagMappings",
                column: "LocationTag",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationTagMappings");
        }
    }
}
