using System;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartSchool.Infrastructure.Persistence;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    default_locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schools", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    preferred_locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    password_changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "academic_years",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_academic_years", x => x.id);
                    table.ForeignKey(
                        name: "fk_academic_years_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.ForeignKey(
                        name: "fk_branches_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grade_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade_levels", x => x.id);
                    table.ForeignKey(
                        name: "fk_grade_levels_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subjects", x => x.id);
                    table.ForeignKey(
                        name: "fk_subjects_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "guardians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardians", x => x.id);
                    table.ForeignKey(
                        name: "fk_guardians_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_guardians_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    context_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_contexts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_contexts", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_contexts_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_contexts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_memberships_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_terms_academic_years_academic_year_id",
                        column: x => x.academic_year_id,
                        principalTable: "academic_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_terms_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_students", x => x.id);
                    table.ForeignKey(
                        name: "fk_students_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_students_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_students_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_class_sections_academic_years_academic_year_id",
                        column: x => x.academic_year_id,
                        principalTable: "academic_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_class_sections_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_class_sections_grade_levels_grade_level_id",
                        column: x => x.grade_level_id,
                        principalTable: "grade_levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_class_sections_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "guardian_students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    can_view_fees = table.Column<bool>(type: "boolean", nullable: false),
                    can_request_leave = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardian_students", x => x.id);
                    table.ForeignKey(
                        name: "fk_guardian_students_guardians_guardian_id",
                        column: x => x.guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_guardian_students_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_guardian_students_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_sessions_class_sections_class_section_id",
                        column: x => x.class_section_id,
                        principalTable: "class_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_sessions_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_enrollments_academic_years_academic_year_id",
                        column: x => x.academic_year_id,
                        principalTable: "academic_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollments_class_sections_class_section_id",
                        column: x => x.class_section_id,
                        principalTable: "class_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollments_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "timetable_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    room = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timetable_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_timetable_entries_class_sections_class_section_id",
                        column: x => x.class_section_id,
                        principalTable: "class_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_timetable_entries_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_timetable_entries_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_records_attendance_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "attendance_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_records_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_records_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_academic_years_school_id",
                table: "academic_years",
                column: "school_id",
                unique: true,
                filter: "is_current = true");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_school_id",
                table: "attendance_records",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_session_id_student_id",
                table: "attendance_records",
                columns: new[] { "session_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_student_id",
                table: "attendance_records",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_sessions_class_section_id_session_date",
                table: "attendance_sessions",
                columns: new[] { "class_section_id", "session_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_sessions_school_id",
                table: "attendance_sessions",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_school_id_created_at_utc",
                table: "audit_events",
                columns: new[] { "school_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_school_id_name",
                table: "branches",
                columns: new[] { "school_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_sections_academic_year_id",
                table: "class_sections",
                column: "academic_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sections_branch_id",
                table: "class_sections",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sections_grade_level_id",
                table: "class_sections",
                column: "grade_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sections_school_id",
                table: "class_sections",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_academic_year_id",
                table: "enrollments",
                column: "academic_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_class_section_id",
                table: "enrollments",
                column: "class_section_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_school_id",
                table: "enrollments",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_student_id_academic_year_id",
                table: "enrollments",
                columns: new[] { "student_id", "academic_year_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grade_levels_school_id",
                table: "grade_levels",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardian_students_guardian_id_student_id",
                table: "guardian_students",
                columns: new[] { "guardian_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guardian_students_school_id",
                table: "guardian_students",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardian_students_student_id",
                table: "guardian_students",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardians_school_id_user_id",
                table: "guardians",
                columns: new[] { "school_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guardians_user_id",
                table: "guardians",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_students_branch_id",
                table: "students",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_students_school_id_student_code",
                table: "students",
                columns: new[] { "school_id", "student_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_students_user_id",
                table: "students",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_subjects_school_id",
                table: "subjects",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_terms_academic_year_id",
                table: "terms",
                column: "academic_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_terms_school_id",
                table: "terms",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_class_section_id_day_of_week_starts_at",
                table: "timetable_entries",
                columns: new[] { "class_section_id", "day_of_week", "starts_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_school_id",
                table: "timetable_entries",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_subject_id",
                table: "timetable_entries",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_contexts_school_id",
                table: "user_contexts",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_contexts_user_id_revoked_at_utc",
                table: "user_contexts",
                columns: new[] { "user_id", "revoked_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memberships_school_id",
                table: "user_memberships",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_memberships_user_id_school_id_role",
                table: "user_memberships",
                columns: new[] { "user_id", "school_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.Sql(RlsSql.Up);
            migrationBuilder.Sql(RlsSql.TenantPolicies());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RlsSql.Down);

            migrationBuilder.DropTable(
                name: "attendance_records");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "enrollments");

            migrationBuilder.DropTable(
                name: "guardian_students");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "terms");

            migrationBuilder.DropTable(
                name: "timetable_entries");

            migrationBuilder.DropTable(
                name: "user_contexts");

            migrationBuilder.DropTable(
                name: "user_memberships");

            migrationBuilder.DropTable(
                name: "attendance_sessions");

            migrationBuilder.DropTable(
                name: "guardians");

            migrationBuilder.DropTable(
                name: "students");

            migrationBuilder.DropTable(
                name: "subjects");

            migrationBuilder.DropTable(
                name: "class_sections");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "academic_years");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "grade_levels");

            migrationBuilder.DropTable(
                name: "schools");

            migrationBuilder.Sql(RlsSql.DropRuntimeRole);
        }
    }
}
