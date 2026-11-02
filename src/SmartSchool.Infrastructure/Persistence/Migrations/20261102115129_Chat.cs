using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Chat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    class_section_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_low_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_high_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_conversations", x => x.id);
                    table.UniqueConstraint("ak_chat_conversations_id_school_id", x => new { x.id, x.school_id });
                    table.ForeignKey(
                        name: "fk_chat_conversations_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_hidden",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_hidden", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_hidden_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_pins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pinned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_pins", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_pins_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_presence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_presence", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_presence_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_reads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reads", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_reads_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    payload = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_conversations_conversation_id_school_id",
                        columns: x => new { x.conversation_id, x.school_id },
                        principalTable: "chat_conversations",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_messages_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_school_id_class_section_id",
                table: "chat_conversations",
                columns: new[] { "school_id", "class_section_id" },
                unique: true,
                filter: "kind = 'group'");

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_school_id_user_low_id_user_high_id",
                table: "chat_conversations",
                columns: new[] { "school_id", "user_low_id", "user_high_id" },
                unique: true,
                filter: "kind = 'direct'");

            migrationBuilder.CreateIndex(
                name: "ix_chat_hidden_conversation_id_user_id",
                table: "chat_hidden",
                columns: new[] { "conversation_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_hidden_school_id",
                table: "chat_hidden",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_conversation_id_created_at_utc",
                table: "chat_messages",
                columns: new[] { "conversation_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_conversation_id_school_id",
                table: "chat_messages",
                columns: new[] { "conversation_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_school_id",
                table: "chat_messages",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_pins_conversation_id_user_id",
                table: "chat_pins",
                columns: new[] { "conversation_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_pins_school_id",
                table: "chat_pins",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_presence_school_id_user_id",
                table: "chat_presence",
                columns: new[] { "school_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_reads_conversation_id_user_id",
                table: "chat_reads",
                columns: new[] { "conversation_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_reads_school_id",
                table: "chat_reads",
                column: "school_id");

            migrationBuilder.Sql("""
                ALTER TABLE chat_conversations
                  ADD CONSTRAINT ck_chat_conversations_kind
                  CHECK (kind IN ('group', 'direct'));
                ALTER TABLE chat_messages
                  ADD CONSTRAINT ck_chat_messages_kind
                  CHECK (kind IN ('text', 'voice', 'file', 'image'));

                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
                  chat_conversations,
                  chat_messages,
                  chat_reads,
                  chat_pins,
                  chat_presence,
                  chat_hidden
                TO smart_school_app;

                ALTER TABLE chat_conversations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_conversations FORCE ROW LEVEL SECURITY;
                CREATE POLICY chat_conversations_tenant ON chat_conversations
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);

                ALTER TABLE chat_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY chat_messages_tenant ON chat_messages
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);

                ALTER TABLE chat_reads ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_reads FORCE ROW LEVEL SECURITY;
                CREATE POLICY chat_reads_tenant ON chat_reads
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);

                ALTER TABLE chat_pins ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_pins FORCE ROW LEVEL SECURITY;
                CREATE POLICY chat_pins_tenant ON chat_pins
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);

                ALTER TABLE chat_presence ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_presence FORCE ROW LEVEL SECURITY;
                CREATE POLICY chat_presence_tenant ON chat_presence
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);

                ALTER TABLE chat_hidden ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chat_hidden FORCE ROW LEVEL SECURITY;
                CREATE POLICY chat_hidden_tenant ON chat_hidden
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS chat_conversations_tenant ON chat_conversations;
                DROP POLICY IF EXISTS chat_messages_tenant ON chat_messages;
                DROP POLICY IF EXISTS chat_reads_tenant ON chat_reads;
                DROP POLICY IF EXISTS chat_pins_tenant ON chat_pins;
                DROP POLICY IF EXISTS chat_presence_tenant ON chat_presence;
                DROP POLICY IF EXISTS chat_hidden_tenant ON chat_hidden;
                """);

            migrationBuilder.DropTable(
                name: "chat_hidden");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "chat_pins");

            migrationBuilder.DropTable(
                name: "chat_presence");

            migrationBuilder.DropTable(
                name: "chat_reads");

            migrationBuilder.DropTable(
                name: "chat_conversations");
        }
    }
}
