namespace SmartSchool.Infrastructure.Persistence;

public static class RlsSql
{
    public const string Up = """
        DO $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'smart_school_app') THEN
            CREATE ROLE smart_school_app NOLOGIN NOINHERIT NOBYPASSRLS;
          END IF;
        END
        $$;

        DO $$
        BEGIN
          EXECUTE format('GRANT CONNECT ON DATABASE %I TO smart_school_app', current_database());
        END
        $$;
        GRANT USAGE ON SCHEMA public TO smart_school_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
          users,
          schools,
          branches,
          user_memberships,
          students,
          guardians,
          guardian_students,
          refresh_tokens,
          user_contexts,
          audit_events,
          academic_years,
          terms,
          grade_levels,
          class_sections,
          subjects,
          enrollments,
          timetable_entries,
          attendance_sessions,
          attendance_records
        TO smart_school_app;

        ALTER TABLE schools ENABLE ROW LEVEL SECURITY;
        ALTER TABLE schools FORCE ROW LEVEL SECURITY;
        CREATE POLICY schools_select ON schools FOR SELECT
          USING (
            id IN (
              SELECT m.school_id FROM user_memberships m
              WHERE m.user_id = NULLIF(current_setting('app.user_id', true), '')::uuid
            )
            OR id = NULLIF(current_setting('app.school_id', true), '')::uuid
          );
        CREATE POLICY schools_insert ON schools FOR INSERT
          WITH CHECK (id = NULLIF(current_setting('app.school_id', true), '')::uuid);
        CREATE POLICY schools_update ON schools FOR UPDATE
          USING (id = NULLIF(current_setting('app.school_id', true), '')::uuid)
          WITH CHECK (id = NULLIF(current_setting('app.school_id', true), '')::uuid);
        CREATE POLICY schools_delete ON schools FOR DELETE
          USING (id = NULLIF(current_setting('app.school_id', true), '')::uuid);

        ALTER TABLE user_memberships ENABLE ROW LEVEL SECURITY;
        ALTER TABLE user_memberships FORCE ROW LEVEL SECURITY;
        CREATE POLICY user_memberships_select ON user_memberships FOR SELECT
          USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);
        CREATE POLICY user_memberships_modify ON user_memberships FOR ALL
          USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid)
          WITH CHECK (
            user_id = NULLIF(current_setting('app.user_id', true), '')::uuid
            AND school_id = NULLIF(current_setting('app.school_id', true), '')::uuid
          );

        ALTER TABLE user_contexts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE user_contexts FORCE ROW LEVEL SECURITY;
        CREATE POLICY user_contexts_access ON user_contexts FOR ALL
          USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid)
          WITH CHECK (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);

        ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;
        CREATE POLICY audit_events_select ON audit_events FOR SELECT
          USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);
        CREATE POLICY audit_events_insert ON audit_events FOR INSERT
          WITH CHECK (
            school_id IS NULL
            OR school_id = NULLIF(current_setting('app.school_id', true), '')::uuid
          );

        CREATE OR REPLACE FUNCTION app_school_admin_exists()
        RETURNS boolean
        LANGUAGE sql
        SECURITY DEFINER
        SET search_path = public
        AS $$
          SELECT EXISTS (
            SELECT 1 FROM user_memberships
            WHERE role = 'school_admin' AND status = 'active'
          );
        $$;
        REVOKE ALL ON FUNCTION app_school_admin_exists() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION app_school_admin_exists() TO smart_school_app;
        """;

    public const string TenantTables = """
        SELECT unnest(ARRAY[
          'branches',
          'students',
          'guardians',
          'guardian_students',
          'academic_years',
          'terms',
          'grade_levels',
          'class_sections',
          'subjects',
          'enrollments',
          'timetable_entries',
          'attendance_sessions',
          'attendance_records'
        ]) AS table_name;
        """;

    public static string TenantPolicies()
    {
        var tables = new[]
        {
            "branches",
            "students",
            "guardians",
            "guardian_students",
            "academic_years",
            "terms",
            "grade_levels",
            "class_sections",
            "subjects",
            "enrollments",
            "timetable_entries",
            "attendance_sessions",
            "attendance_records"
        };

        return string.Join('\n', tables.Select(table => $"""
            ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {table}_tenant ON {table}
              USING (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid)
              WITH CHECK (school_id = NULLIF(current_setting('app.school_id', true), '')::uuid);
            """));
    }

    public const string Down = """
        DROP POLICY IF EXISTS schools_select ON schools;
        DROP POLICY IF EXISTS schools_insert ON schools;
        DROP POLICY IF EXISTS schools_update ON schools;
        DROP POLICY IF EXISTS schools_delete ON schools;
        DROP POLICY IF EXISTS user_memberships_select ON user_memberships;
        DROP POLICY IF EXISTS user_memberships_modify ON user_memberships;
        DROP POLICY IF EXISTS user_contexts_access ON user_contexts;
        DROP POLICY IF EXISTS audit_events_select ON audit_events;
        DROP POLICY IF EXISTS audit_events_insert ON audit_events;
        DROP POLICY IF EXISTS branches_tenant ON branches;
        DROP POLICY IF EXISTS students_tenant ON students;
        DROP POLICY IF EXISTS guardians_tenant ON guardians;
        DROP POLICY IF EXISTS guardian_students_tenant ON guardian_students;
        DROP POLICY IF EXISTS academic_years_tenant ON academic_years;
        DROP POLICY IF EXISTS terms_tenant ON terms;
        DROP POLICY IF EXISTS grade_levels_tenant ON grade_levels;
        DROP POLICY IF EXISTS class_sections_tenant ON class_sections;
        DROP POLICY IF EXISTS subjects_tenant ON subjects;
        DROP POLICY IF EXISTS enrollments_tenant ON enrollments;
        DROP POLICY IF EXISTS timetable_entries_tenant ON timetable_entries;
        DROP POLICY IF EXISTS attendance_sessions_tenant ON attendance_sessions;
        DROP POLICY IF EXISTS attendance_records_tenant ON attendance_records;
        DROP FUNCTION IF EXISTS app_school_admin_exists();
        REVOKE ALL ON ALL TABLES IN SCHEMA public FROM smart_school_app;
        DO $$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'smart_school_app') THEN
            EXECUTE format('REVOKE ALL ON DATABASE %I FROM smart_school_app', current_database());
            EXECUTE format('REVOKE ALL ON SCHEMA public FROM smart_school_app');
          END IF;
        END
        $$;
        """;

    public const string DropRuntimeRole = """
        DROP ROLE IF EXISTS smart_school_app;
        """;
}
