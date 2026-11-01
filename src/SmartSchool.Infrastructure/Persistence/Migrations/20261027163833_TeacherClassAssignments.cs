using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeacherClassAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_subjects_id_school_id",
                table: "subjects",
                columns: new[] { "id", "school_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_class_sections_id_school_id",
                table: "class_sections",
                columns: new[] { "id", "school_id" });

            migrationBuilder.CreateTable(
                name: "teacher_class_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_class_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_class_assignments_class_sections_class_section_id_s",
                        columns: x => new { x.class_section_id, x.school_id },
                        principalTable: "class_sections",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teacher_class_assignments_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teacher_class_assignments_subjects_subject_id_school_id",
                        columns: x => new { x.subject_id, x.school_id },
                        principalTable: "subjects",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teacher_class_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_class_assignments_class_section_id_school_id",
                table: "teacher_class_assignments",
                columns: new[] { "class_section_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_class_assignments_school_id",
                table: "teacher_class_assignments",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_class_assignments_subject_id_school_id",
                table: "teacher_class_assignments",
                columns: new[] { "subject_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_class_assignments_user_id_class_section_id_subject_",
                table: "teacher_class_assignments",
                columns: new[] { "user_id", "class_section_id", "subject_id" },
                unique: true);

            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE teacher_class_assignments TO smart_school_app;
                ALTER TABLE teacher_class_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE teacher_class_assignments FORCE ROW LEVEL SECURITY;
                CREATE POLICY teacher_class_assignments_tenant ON teacher_class_assignments
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS teacher_class_assignments_tenant ON teacher_class_assignments;");
            migrationBuilder.DropTable(
                name: "teacher_class_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_subjects_id_school_id",
                table: "subjects");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_class_sections_id_school_id",
                table: "class_sections");
        }
    }
}
