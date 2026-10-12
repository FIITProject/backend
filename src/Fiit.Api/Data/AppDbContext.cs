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
    public DbSet<InviteForm> InviteForms => Set<InviteForm>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Homework> Homeworks => Set<Homework>();
    public DbSet<HomeworkTarget> HomeworkTargets => Set<HomeworkTarget>();
    public DbSet<Subtask> Subtasks => Set<Subtask>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SubtaskScore> SubtaskScores => Set<SubtaskScore>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Teacher>(e =>
        {
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Login).HasMaxLength(100);
            e.Property(x => x.FullName).HasMaxLength(200);
        });

        b.Entity<Subject>().Property(x => x.Name).HasMaxLength(200);

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
            e.Property(x => x.SemesterTerm).HasConversion<string>().HasMaxLength(10);
            e.HasOne(x => x.AdminTeacher).WithMany().OnDelete(DeleteBehavior.Restrict);
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
        });

        b.Entity<Enrollment>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Tag>(e =>
        {
            e.HasIndex(x => new { x.TeacherId, x.Name }).IsUnique();
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId);
        });

        b.Entity<StudentTag>(e =>
        {
            e.HasKey(x => new { x.TagId, x.StudentId });
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
        });

        b.Entity<InviteForm>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId);
            e.HasMany(x => x.Courses).WithMany().UsingEntity("invite_form_courses");
        });

        b.Entity<Application>(e =>
        {
            e.HasIndex(x => x.BindCode).IsUnique();
            e.HasOne<InviteForm>().WithMany().HasForeignKey(x => x.FormId);
        });

        b.Entity<Homework>(e =>
        {
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId);
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.AuthorId);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.SemesterTerm).HasConversion<string>().HasMaxLength(10);
            e.HasMany(x => x.Courses).WithMany().UsingEntity("homework_courses");
        });

        b.Entity<HomeworkTarget>().Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);

        b.Entity<Subtask>().Property(x => x.MaxScore).HasPrecision(8, 2);

        b.Entity<Submission>(e =>
        {
            e.HasIndex(x => new { x.HomeworkId, x.StudentId, x.Version }).IsUnique();
            e.HasOne(x => x.Review).WithOne().HasForeignKey<Review>(x => x.SubmissionId);
        });

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Annotations).HasColumnType("jsonb");
            e.Property(x => x.TotalOverride).HasPrecision(8, 2);
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId);
        });

        b.Entity<SubtaskScore>(e =>
        {
            e.HasKey(x => new { x.ReviewId, x.SubtaskId });
            e.HasOne<Review>().WithMany(x => x.SubtaskScores).HasForeignKey(x => x.ReviewId);
            e.HasOne<Subtask>().WithMany().HasForeignKey(x => x.SubtaskId);
            e.Property(x => x.Score).HasPrecision(8, 2);
        });

        b.Entity<Notification>(e =>
        {
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.AvailableAt });
        });
    }
}
