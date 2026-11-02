using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartSchool.Core;

namespace SmartSchool.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IDbSession session) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserMembership> UserMemberships => Set<UserMembership>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<GuardianStudent> GuardianStudents => Set<GuardianStudent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserContext> UserContexts => Set<UserContext>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<GradeLevel> GradeLevels => Set<GradeLevel>();
    public DbSet<ClassSection> ClassSections => Set<ClassSection>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TeacherClassAssignment> TeacherClassAssignments => Set<TeacherClassAssignment>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<HomeworkAssignment> HomeworkAssignments => Set<HomeworkAssignment>();
    public DbSet<HomeworkSubmission> HomeworkSubmissions => Set<HomeworkSubmission>();
    public DbSet<SchoolTrip> SchoolTrips => Set<SchoolTrip>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatRead> ChatReads => Set<ChatRead>();
    public DbSet<ChatPin> ChatPins => Set<ChatPin>();
    public DbSet<ChatPresence> ChatPresences => Set<ChatPresence>();
    public DbSet<ChatHidden> ChatHiddens => Set<ChatHidden>();

    private Guid CurrentSchoolId => session.SchoolId;

    internal Guid SessionUserId => session.UserId;

    internal Guid SessionSchoolId => session.SchoolId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var role = new ValueConverter<AppRole, string>(v => RoleNames.ToName(v), v => ParseRole(v));
        var userStatus = new ValueConverter<UserStatus, string>(v => ToToken(v), v => Parse<UserStatus>(v));
        var membershipStatus = new ValueConverter<MembershipStatus, string>(v => ToToken(v), v => Parse<MembershipStatus>(v));
        var attendance = new ValueConverter<AttendanceMark, string>(v => ToToken(v), v => Parse<AttendanceMark>(v));

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.PreferredLocale).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasConversion(userStatus).HasMaxLength(32);
            entity.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<School>(entity =>
        {
            entity.ToTable("schools");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DefaultLocale).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<UserMembership>(entity =>
        {
            entity.ToTable("user_memberships");
            entity.Property(x => x.Role).HasConversion(role).HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion(membershipStatus).HasMaxLength(32);
            entity.HasIndex(x => new { x.UserId, x.SchoolId, x.Role }).IsUnique();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.ToTable("students");
            entity.Property(x => x.StudentCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.HasAlternateKey(x => new { x.Id, x.SchoolId });
            entity.HasIndex(x => new { x.SchoolId, x.StudentCode }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<Guardian>(entity =>
        {
            entity.ToTable("guardians");
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.SchoolId, x.UserId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<GuardianStudent>(entity =>
        {
            entity.ToTable("guardian_students");
            entity.Property(x => x.Relationship).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.GuardianId, x.StudentId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Guardian>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.FamilyId);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserContext>(entity =>
        {
            entity.ToTable("user_contexts");
            entity.Property(x => x.Role).HasConversion(role).HasMaxLength(32);
            entity.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.SchoolId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<AcademicYear>(entity =>
        {
            entity.ToTable("academic_years");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.SchoolId).IsUnique().HasFilter("is_current = true");
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<Term>(entity =>
        {
            entity.ToTable("terms");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<GradeLevel>(entity =>
        {
            entity.ToTable("grade_levels");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ClassSection>(entity =>
        {
            entity.ToTable("class_sections");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasAlternateKey(x => new { x.Id, x.SchoolId });
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GradeLevel>().WithMany().HasForeignKey(x => x.GradeLevelId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<Subject>(entity =>
        {
            entity.ToTable("subjects");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasAlternateKey(x => new { x.Id, x.SchoolId });
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<TeacherClassAssignment>(entity =>
        {
            entity.ToTable("teacher_class_assignments");
            entity.HasIndex(x => new { x.UserId, x.ClassSectionId, x.SubjectId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClassSection>().WithMany().HasForeignKey(x => new { x.ClassSectionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Subject>().WithMany().HasForeignKey(x => new { x.SubjectId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("enrollments");
            entity.HasIndex(x => new { x.StudentId, x.AcademicYearId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClassSection>().WithMany().HasForeignKey(x => x.ClassSectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<TimetableEntry>(entity =>
        {
            entity.ToTable("timetable_entries");
            entity.Property(x => x.Room).HasMaxLength(50);
            entity.HasIndex(x => new { x.ClassSectionId, x.DayOfWeek, x.StartsAt }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClassSection>().WithMany().HasForeignKey(x => x.ClassSectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<AttendanceSession>(entity =>
        {
            entity.ToTable("attendance_sessions");
            entity.HasAlternateKey(x => new { x.Id, x.SchoolId });
            entity.HasIndex(x => new { x.ClassSectionId, x.SessionDate }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClassSection>().WithMany().HasForeignKey(x => new { x.ClassSectionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<HomeworkAssignment>(entity =>
        {
            entity.ToTable("homework_assignments");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Instructions).HasMaxLength(4000).IsRequired();
            entity.HasAlternateKey(x => new { x.Id, x.SchoolId });
            entity.HasIndex(x => new { x.ClassSectionId, x.DueOn });
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClassSection>().WithMany().HasForeignKey(x => new { x.ClassSectionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Subject>().WithMany().HasForeignKey(x => new { x.SubjectId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<HomeworkSubmission>(entity =>
        {
            entity.ToTable("homework_submissions");
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(2000);
            entity.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HomeworkAssignment>().WithMany().HasForeignKey(x => new { x.AssignmentId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Student>().WithMany().HasForeignKey(x => new { x.StudentId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<SchoolTrip>(entity =>
        {
            entity.ToTable("school_trips");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subtitle).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SupervisorName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.SchoolId, x.TripOn });
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.ToTable("chat_conversations");
            entity.HasAlternateKey(x => new { x.Id, x.SchoolId });
            entity.Property(x => x.Kind).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => new { x.SchoolId, x.ClassSectionId })
                .IsUnique()
                .HasFilter("kind = 'group'");
            entity.HasIndex(x => new { x.SchoolId, x.UserLowId, x.UserHighId })
                .IsUnique()
                .HasFilter("kind = 'direct'");
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.Property(x => x.Kind).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(260);
            entity.Property(x => x.ContentType).HasMaxLength(120);
            entity.Property(x => x.ReplySenderName).HasMaxLength(200);
            entity.Property(x => x.ReplyBody).HasMaxLength(280);
            entity.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChatConversation>().WithMany().HasForeignKey(x => new { x.ConversationId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ChatRead>(entity =>
        {
            entity.ToTable("chat_reads");
            entity.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ChatPin>(entity =>
        {
            entity.ToTable("chat_pins");
            entity.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ChatPresence>(entity =>
        {
            entity.ToTable("chat_presence");
            entity.HasIndex(x => new { x.SchoolId, x.UserId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<ChatHidden>(entity =>
        {
            entity.ToTable("chat_hidden");
            entity.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });

        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.ToTable("attendance_records");
            entity.Property(x => x.Status).HasConversion(attendance).HasMaxLength(32);
            entity.HasIndex(x => new { x.SessionId, x.StudentId }).IsUnique();
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AttendanceSession>().WithMany().HasForeignKey(x => new { x.SessionId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Student>().WithMany().HasForeignKey(x => new { x.StudentId, x.SchoolId })
                .HasPrincipalKey(x => new { x.Id, x.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => x.SchoolId == CurrentSchoolId);
        });
    }

    private static AppRole ParseRole(string value)
    {
        if (!RoleNames.TryParse(value, out var role))
            throw new InvalidOperationException("Stored role is not recognized.");
        return role;
    }

    private static string ToToken<TEnum>(TEnum value) where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static TEnum Parse<TEnum>(string value) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            return parsed;
        throw new InvalidOperationException("Stored enum value is not recognized.");
    }
}
