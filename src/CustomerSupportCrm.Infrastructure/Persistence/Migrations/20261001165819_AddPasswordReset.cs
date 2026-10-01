using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_reset_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_reset_token_hash",
                table: "users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_reset_expires_at",
                table: "customer_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_reset_token_hash",
                table: "customer_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sessions_valid_from",
                table: "customer_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_password_reset_token_hash",
                table: "users",
                column: "password_reset_token_hash",
                filter: "password_reset_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customer_accounts_password_reset_token_hash",
                table: "customer_accounts",
                column: "password_reset_token_hash",
                filter: "password_reset_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_password_reset_token_hash",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_customer_accounts_password_reset_token_hash",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "password_reset_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_reset_token_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_reset_expires_at",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "password_reset_token_hash",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "sessions_valid_from",
                table: "customer_accounts");
        }
    }
}
