using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatVoteKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE chat_messages DROP CONSTRAINT ck_chat_messages_kind;
                ALTER TABLE chat_messages
                  ADD CONSTRAINT ck_chat_messages_kind
                  CHECK (kind IN ('text', 'voice', 'file', 'image', 'vote'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE chat_messages DROP CONSTRAINT ck_chat_messages_kind;
                ALTER TABLE chat_messages
                  ADD CONSTRAINT ck_chat_messages_kind
                  CHECK (kind IN ('text', 'voice', 'file', 'image'));
                """);
        }
    }
}
