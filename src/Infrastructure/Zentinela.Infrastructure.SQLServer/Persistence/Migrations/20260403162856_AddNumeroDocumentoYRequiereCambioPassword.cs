using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentinela.Infrastructure.SQLServer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNumeroDocumentoYRequiereCambioPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NumeroDocumento",
                table: "AspNetUsers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "RequiereCambioPassword",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumeroDocumento",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RequiereCambioPassword",
                table: "AspNetUsers");
        }
    }
}
