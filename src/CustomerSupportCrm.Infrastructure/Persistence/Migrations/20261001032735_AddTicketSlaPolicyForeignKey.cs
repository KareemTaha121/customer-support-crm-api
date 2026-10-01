using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketSlaPolicyForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Policies deleted before this FK existed left dangling ids on tickets; clear them so the constraint can be added.
            migrationBuilder.Sql(
                """
                UPDATE tickets t
                SET sla_policy_id = NULL
                WHERE t.sla_policy_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM sla_policies p WHERE p.id = t.sla_policy_id);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_sla_policy_id",
                table: "tickets",
                column: "sla_policy_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_sla_policies_sla_policy_id",
                table: "tickets",
                column: "sla_policy_id",
                principalTable: "sla_policies",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_sla_policies_sla_policy_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_sla_policy_id",
                table: "tickets");
        }
    }
}
