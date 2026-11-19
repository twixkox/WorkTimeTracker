using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Boat> Boats => Set<Boat>();
    public DbSet<WorkType> WorkTypes => Set<WorkType>();
    public DbSet<WorkDay> WorkDays => Set<WorkDay>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApplicationLog> ApplicationLogs => Set<ApplicationLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EngineeringTask> EngineeringTasks => Set<EngineeringTask>();
    public DbSet<TelegramLinkCode> TelegramLinkCodes => Set<TelegramLinkCode>();
    public DbSet<EngineeringTaskComment> EngineeringTaskComments => Set<EngineeringTaskComment>();
    public DbSet<EngineeringTaskAttachment> EngineeringTaskAttachments => Set<EngineeringTaskAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Employee>()
            .HasIndex(e => e.UserId);

        builder.Entity<WorkDay>()
            .HasIndex(w => new { w.EmployeeId, w.Date })
            .IsUnique();

        builder.Entity<WorkDay>()
            .HasMany(w => w.TimeEntries)
            .WithOne(t => t.WorkDay)
            .HasForeignKey(t => t.WorkDayId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TimeEntry>()
            .Property(t => t.Hours)
            .HasPrecision(5, 2);

        builder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        builder.Entity<ApplicationLog>()
            .HasIndex(l => new { l.Level, l.Timestamp });
        builder.Entity<ApplicationLog>()
            .Property(l => l.Message)
            .HasMaxLength(4000);
        builder.Entity<ApplicationLog>()
            .Property(l => l.Category)
            .HasMaxLength(500);

        builder.Entity<EngineeringTask>()
            .Property(t => t.CreatedByUserId)
            .HasMaxLength(450);
        builder.Entity<EngineeringTask>()
            .Property(t => t.CreatedByName)
            .HasMaxLength(200);
        builder.Entity<EngineeringTask>()
            .Property(t => t.Title)
            .HasMaxLength(1000);
        builder.Entity<EngineeringTask>()
            .Property(t => t.AssigneeId)
            .HasMaxLength(450);
        builder.Entity<EngineeringTask>()
            .Property(t => t.HullNumber)
            .HasMaxLength(100);
        builder.Entity<EngineeringTask>()
            .HasIndex(t => t.CreatedAt);
        builder.Entity<EngineeringTask>()
            .HasIndex(t => t.AssigneeId);
        builder.Entity<EngineeringTask>()
            .HasIndex(t => t.Status);
        builder.Entity<EngineeringTask>()
            .HasIndex(t => t.Priority);
        builder.Entity<EngineeringTask>()
            .HasIndex(t => t.Deadline);
        builder.Entity<EngineeringTask>()
            .HasIndex(t => t.BoatId);
        builder.Entity<EngineeringTask>()
            .HasOne(t => t.Assignee)
            .WithMany()
            .HasForeignKey(t => t.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<EngineeringTask>()
            .HasOne(t => t.Boat)
            .WithMany()
            .HasForeignKey(t => t.BoatId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<EngineeringTask>()
            .HasMany(t => t.Comments)
            .WithOne(c => c.EngineeringTask)
            .HasForeignKey(c => c.EngineeringTaskId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<EngineeringTask>()
            .HasMany(t => t.Attachments)
            .WithOne(a => a.EngineeringTask)
            .HasForeignKey(a => a.EngineeringTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ApplicationUser>()
            .Property(u => u.TelegramUsername)
            .HasMaxLength(256);
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.TelegramUserId)
            .IsUnique()
            .HasFilter("[TelegramUserId] IS NOT NULL");

        builder.Entity<TelegramLinkCode>()
            .Property(c => c.UserId)
            .HasMaxLength(450);
        builder.Entity<TelegramLinkCode>()
            .Property(c => c.CodeHash)
            .HasMaxLength(64);
        builder.Entity<TelegramLinkCode>()
            .HasIndex(c => c.UserId);
        builder.Entity<TelegramLinkCode>()
            .HasIndex(c => c.CodeHash)
            .IsUnique();
        builder.Entity<TelegramLinkCode>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EngineeringTaskComment>()
            .Property(c => c.AuthorUserId)
            .HasMaxLength(450);
        builder.Entity<EngineeringTaskComment>()
            .Property(c => c.AuthorName)
            .HasMaxLength(200);
        builder.Entity<EngineeringTaskComment>()
            .Property(c => c.Text)
            .HasMaxLength(4000);
        builder.Entity<EngineeringTaskComment>()
            .HasIndex(c => c.EngineeringTaskId);

        builder.Entity<EngineeringTaskAttachment>()
            .Property(a => a.FileName)
            .HasMaxLength(255);
        builder.Entity<EngineeringTaskAttachment>()
            .Property(a => a.StoredFileName)
            .HasMaxLength(255);
        builder.Entity<EngineeringTaskAttachment>()
            .Property(a => a.ContentType)
            .HasMaxLength(255);
        builder.Entity<EngineeringTaskAttachment>()
            .Property(a => a.UploadedByUserId)
            .HasMaxLength(450);
        builder.Entity<EngineeringTaskAttachment>()
            .Property(a => a.UploadedByName)
            .HasMaxLength(200);
        builder.Entity<EngineeringTaskAttachment>()
            .HasIndex(a => a.EngineeringTaskId);
    }
}
