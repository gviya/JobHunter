using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDescriptionIsSnippetAndNullableClientFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsClientFinal",
                table: "JobOffers",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<bool>(
                name: "DescriptionIsSnippet",
                table: "JobOffers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionIsSnippet",
                table: "JobOffers");

            // La columna vuelve a ser NOT NULL: "no se sabe" se degrada a false.
            migrationBuilder.Sql("UPDATE JobOffers SET IsClientFinal = 0 WHERE IsClientFinal IS NULL;");

            migrationBuilder.AlterColumn<bool>(
                name: "IsClientFinal",
                table: "JobOffers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
