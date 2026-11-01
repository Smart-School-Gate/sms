using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HomeworkAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "homework_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_homework_assignments", x => x.id);
                    table.UniqueConstraint("ak_homework_assignments_id_school_id", x => new { x.id, x.school_id });
                    table.ForeignKey(
                        name: "fk_homework_assignments_class_sections_class_section_id_school",
                        columns: x => new { x.class_section_id, x.school_id },
                        principalTable: "class_sections",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_homework_assignments_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_homework_assignments_subjects_subject_id_school_id",
                        columns: x => new { x.subject_id, x.school_id },
                        principalTable: "subjects",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_homework_assignments_users_teacher_user_id",
                        column: x => x.teacher_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "homework_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_homework_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_homework_submissions_homework_assignments_assignment_id_sch",
                        columns: x => new { x.assignment_id, x.school_id },
                        principalTable: "homework_assignments",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_homework_submissions_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_homework_submissions_students_student_id_school_id",
                        columns: x => new { x.student_id, x.school_id },
                        principalTable: "students",
                        principalColumns: new[] { "id", "school_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_homework_assignments_class_section_id_due_on",
                table: "homework_assignments",
                columns: new[] { "class_section_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_homework_assignments_class_section_id_school_id",
                table: "homework_assignments",
                columns: new[] { "class_section_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_homework_assignments_school_id",
                table: "homework_assignments",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_homework_assignments_subject_id_school_id",
                table: "homework_assignments",
                columns: new[] { "subject_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_homework_assignments_teacher_user_id",
                table: "homework_assignments",
                column: "teacher_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_homework_submissions_assignment_id_school_id",
                table: "homework_submissions",
                columns: new[] { "assignment_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_homework_submissions_assignment_id_student_id",
                table: "homework_submissions",
                columns: new[] { "assignment_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_homework_submissions_school_id",
                table: "homework_submissions",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_homework_submissions_student_id_school_id",
                table: "homework_submissions",
                columns: new[] { "student_id", "school_id" });

            migrationBuilder.Sql("""
                ALTER TABLE homework_submissions
                  ADD CONSTRAINT ck_homework_submissions_status
                  CHECK (status IN ('submitted', 'reviewing', 'reviewed'));

                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE homework_assignments TO smart_school_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE homework_submissions TO smart_school_app;

                ALTER TABLE homework_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE homework_assignments FORCE ROW LEVEL SECURITY;
                CREATE POLICY homework_assignments_tenant ON homework_assignments
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);

                ALTER TABLE homework_submissions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE homework_submissions FORCE ROW LEVEL SECURITY;
                CREATE POLICY homework_submissions_tenant ON homework_submissions
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS homework_submissions_tenant ON homework_submissions;
                DROP POLICY IF EXISTS homework_assignments_tenant ON homework_assignments;
                """);

            migrationBuilder.DropTable(
                name: "homework_submissions");

            migrationBuilder.DropTable(
                name: "homework_assignments");
        }
    }
}
