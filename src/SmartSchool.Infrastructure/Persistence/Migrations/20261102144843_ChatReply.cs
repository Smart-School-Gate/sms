using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reply_body",
                table: "chat_messages",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reply_sender_name",
                table: "chat_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reply_to_message_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reply_body",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "reply_sender_name",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "reply_to_message_id",
                table: "chat_messages");
        }
    }
}
