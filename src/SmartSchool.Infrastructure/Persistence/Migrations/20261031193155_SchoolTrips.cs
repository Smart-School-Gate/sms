using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchoolTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "school_trips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    student_count = table.Column<int>(type: "integer", nullable: false),
                    supervisor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trip_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_trips", x => x.id);
                    table.ForeignKey(
                        name: "fk_school_trips_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_school_trips_school_id_trip_on",
                table: "school_trips",
                columns: new[] { "school_id", "trip_on" });

            migrationBuilder.Sql("""
                ALTER TABLE school_trips
                  ADD CONSTRAINT ck_school_trips_status
                  CHECK (status IN ('current', 'completed'));

                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE school_trips TO smart_school_app;

                ALTER TABLE school_trips ENABLE ROW LEVEL SECURITY;
                ALTER TABLE school_trips FORCE ROW LEVEL SECURITY;
                CREATE POLICY school_trips_tenant ON school_trips
                  USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
                  WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS school_trips_tenant ON school_trips;
                """);

            migrationBuilder.DropTable(
                name: "school_trips");
        }
    }
}
