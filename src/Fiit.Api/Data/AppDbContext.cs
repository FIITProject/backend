using Fiit.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fiit.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Subgroup> Subgroups => Set<Subgroup>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseAccess> CourseAccesses => Set<CourseAccess>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<StudentTag> StudentTags => Set<StudentTag>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Homework> Homeworks => Set<Homework>();
    public DbSet<HomeworkTarget> HomeworkTargets => Set<HomeworkTarget>();
    public DbSet<HomeworkRecipient> HomeworkRecipients => Set<HomeworkRecipient>();
    public DbSet<Subtask> Subtasks => Set<Subtask>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SubtaskScore> SubtaskScores => Set<SubtaskScore>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Все enum-ы храним строками — читаемо в БД и не ломается от перестановки значений
        b.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Teacher>(e =>
        {
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Login).HasMaxLength(100);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.HasOne(x => x.AddedBy).WithMany().HasForeignKey(x => x.AddedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Subject>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.NormalizedName).HasMaxLength(200);
            e.HasIndex(x => x.NormalizedName).IsUnique();
        });

        b.Entity<Group>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(50);
        });

        // NULLS NOT DISTINCT: у группы может быть только одна «подгруппа без номера»
        b.Entity<Subgroup>().HasIndex(x => new { x.GroupId, x.Number }).IsUnique().AreNullsDistinct(false);

        b.Entity<Course>(e =>
        {
            e.HasIndex(x => new { x.SubjectId, x.SubgroupId, x.SemesterYear, x.SemesterTerm }).IsUnique();
            e.HasOne(x => x.AdminTeacher).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Semester);
        });

        b.Entity<CourseAccess>(e =>
        {
            e.HasKey(x => new { x.CourseId, x.TeacherId });
            e.HasOne<Course>().WithMany(x => x.Accesses).HasForeignKey(x => x.CourseId);
        });

        b.Entity<Student>(e =>
        {
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.TelegramUsername).HasMaxLength(100);
        });

        b.Entity<Enrollment>(e =>
        {
            // Активная заявка (ожидает или подтверждена) — не больше одной на студента и курс (П-5.7)
            e.HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique().HasFilter("status IN ('Pending', 'Approved')");
            e.HasOne(x => x.Course).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Invite>().WithMany().HasForeignKey(x => x.InviteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RejectReason).HasMaxLength(500);
        });

        b.Entity<Tag>(e =>
        {
            e.HasIndex(x => new { x.TeacherId, x.Name }).IsUnique();
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        b.Entity<StudentTag>(e =>
        {
            e.HasKey(x => new { x.TagId, x.StudentId });
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
        });

        b.Entity<Invite>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.HasOne(x => x.Author).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Courses).WithMany().UsingEntity("invite_courses");
        });

        b.Entity<Homework>(e =>
        {
            e.HasOne(x => x.Subject).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Author).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Link).HasMaxLength(2000);
            e.HasMany(x => x.Courses).WithMany().UsingEntity("homework_courses");
            e.HasIndex(x => new { x.SubjectId, x.SemesterYear, x.SemesterTerm });
        });

        b.Entity<HomeworkTarget>().HasOne<Homework>().WithMany(x => x.Targets).HasForeignKey(x => x.HomeworkId);

        b.Entity<HomeworkRecipient>(e =>
        {
            e.HasKey(x => new { x.HomeworkId, x.StudentId });
            e.HasOne<Homework>().WithMany().HasForeignKey(x => x.HomeworkId);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
        });

        b.Entity<Subtask>(e =>
        {
            e.HasOne<Homework>().WithMany(x => x.Subtasks).HasForeignKey(x => x.HomeworkId);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.MaxScore).HasPrecision(8, 2);
        });

        b.Entity<Submission>(e =>
        {
            e.HasIndex(x => new { x.HomeworkId, x.StudentId, x.Version }).IsUnique();
            e.HasOne(x => x.Homework).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Review).WithOne().HasForeignKey<Review>(x => x.SubmissionId);
            e.Property(x => x.FileName).HasMaxLength(300);
        });

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Annotations).HasColumnType("jsonb");
            e.Property(x => x.SentSubtasks).HasColumnType("jsonb");
            e.Property(x => x.TotalOverride).HasPrecision(8, 2);
            e.Property(x => x.SentTotal).HasPrecision(8, 2);
            e.HasOne(x => x.UpdatedBy).WithMany().HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SentBy).WithMany().HasForeignKey(x => x.SentById).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.IsSent);
            e.Ignore(x => x.HasUnsentChanges);
        });

        b.Entity<SubtaskScore>(e =>
        {
            e.HasKey(x => new { x.ReviewId, x.SubtaskId });
            e.HasOne<Review>().WithMany(x => x.SubtaskScores).HasForeignKey(x => x.ReviewId);
            e.HasOne<Subtask>().WithMany().HasForeignKey(x => x.SubtaskId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Score).HasPrecision(8, 2);
        });

        b.Entity<Notification>(e =>
        {
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
            e.HasIndex(x => new { x.Status, x.AvailableAt });
        });
    }
}
