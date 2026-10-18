using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infra.Persistence.Migrations;

public partial class RenameAccountsToEnglish : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_contas", table: "contas");
        migrationBuilder.RenameTable(name: "contas", newName: "accounts");
        migrationBuilder.RenameColumn(name: "nome", table: "accounts", newName: "name");
        migrationBuilder.RenameIndex(name: "IX_contas_cpf", table: "accounts", newName: "IX_accounts_cpf");
        migrationBuilder.RenameIndex(name: "IX_contas_id", table: "accounts", newName: "IX_accounts_id");
        migrationBuilder.RenameSequence(name: "contas_internal_id_seq", newName: "accounts_internal_id_seq");
        migrationBuilder.AddPrimaryKey(name: "PK_accounts", table: "accounts", column: "internal_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_accounts", table: "accounts");
        migrationBuilder.RenameSequence(name: "accounts_internal_id_seq", newName: "contas_internal_id_seq");
        migrationBuilder.RenameIndex(name: "IX_accounts_id", table: "accounts", newName: "IX_contas_id");
        migrationBuilder.RenameIndex(name: "IX_accounts_cpf", table: "accounts", newName: "IX_contas_cpf");
        migrationBuilder.RenameColumn(name: "name", table: "accounts", newName: "nome");
        migrationBuilder.RenameTable(name: "accounts", newName: "contas");
        migrationBuilder.AddPrimaryKey(name: "PK_contas", table: "contas", column: "internal_id");
    }
}
