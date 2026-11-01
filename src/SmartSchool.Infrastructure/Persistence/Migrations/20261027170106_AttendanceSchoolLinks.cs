using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceSchoolLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_attendance_sessions_session_id",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_students_student_id",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_sessions_class_sections_class_section_id",
                table: "attendance_sessions");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_student_id",
                table: "attendance_records");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_students_id_school_id",
                table: "students",
                columns: new[] { "id", "school_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_attendance_sessions_id_school_id",
                table: "attendance_sessions",
                columns: new[] { "id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_sessions_class_section_id_school_id",
                table: "attendance_sessions",
                columns: new[] { "class_section_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_session_id_school_id",
                table: "attendance_records",
                columns: new[] { "session_id", "school_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_student_id_school_id",
                table: "attendance_records",
                columns: new[] { "student_id", "school_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_attendance_sessions_session_id_school_id",
                table: "attendance_records",
                columns: new[] { "session_id", "school_id" },
                principalTable: "attendance_sessions",
                principalColumns: new[] { "id", "school_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_students_student_id_school_id",
                table: "attendance_records",
                columns: new[] { "student_id", "school_id" },
                principalTable: "students",
                principalColumns: new[] { "id", "school_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_sessions_class_sections_class_section_id_school_",
                table: "attendance_sessions",
                columns: new[] { "class_section_id", "school_id" },
                principalTable: "class_sections",
                principalColumns: new[] { "id", "school_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_attendance_sessions_session_id_school_id",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_students_student_id_school_id",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_sessions_class_sections_class_section_id_school_",
                table: "attendance_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_students_id_school_id",
                table: "students");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_attendance_sessions_id_school_id",
                table: "attendance_sessions");

            migrationBuilder.DropIndex(
                name: "ix_attendance_sessions_class_section_id_school_id",
                table: "attendance_sessions");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_session_id_school_id",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_student_id_school_id",
                table: "attendance_records");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_student_id",
                table: "attendance_records",
                column: "student_id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_attendance_sessions_session_id",
                table: "attendance_records",
                column: "session_id",
                principalTable: "attendance_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_students_student_id",
                table: "attendance_records",
                column: "student_id",
                principalTable: "students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_sessions_class_sections_class_section_id",
                table: "attendance_sessions",
                column: "class_section_id",
                principalTable: "class_sections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
