using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestPick.Repositorio.Migrations;

public partial class AdicionarFotoUsuario : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FotoNomeArquivo", table: "Usuario",
            type: "nvarchar(36)", maxLength: 36, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "FotoAtualizadaEm", table: "Usuario",
            type: "datetime2", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FotoNomeArquivo", table: "Usuario");
        migrationBuilder.DropColumn(name: "FotoAtualizadaEm", table: "Usuario");
    }
}
