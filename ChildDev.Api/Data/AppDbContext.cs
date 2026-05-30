using ChildDev.Api.Models.Entities;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Data;

public class AppDbContext : DbContext
{
    private readonly EncryptionService _enc;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        EncryptionService enc,
        ICurrentAccountProvider accounts) : base(options)
    {
        _enc = enc;
        AccountGuid = accounts.GetAccountGuid();
    }

    /// <summary>Current tenant captured at construction. When null, tenant entity queries return no rows.</summary>
    public string? AccountGuid { get; set; }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalProgress> GoalProgresses => Set<GoalProgress>();
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Journal>().HasIndex(j => new { j.AccountFk, j.UpdatedOn });
        modelBuilder.Entity<Goal>().HasIndex(g => new { g.AccountFk, g.UpdatedOn });
        modelBuilder.Entity<GoalProgress>().HasIndex(p => new { p.AccountFk, p.UpdatedOn });
        modelBuilder.Entity<GoalProgress>().HasIndex(p => new { p.AccountFk, p.GoalFk });
        modelBuilder.Entity<Todo>().HasIndex(t => new { t.AccountFk, t.UpdatedOn });
        modelBuilder.Entity<Account>().HasIndex(a => a.NickName).IsUnique();
        modelBuilder.Entity<AnalyticsEvent>().HasIndex(e => new { e.AccountGuid, e.Timestamp });
        modelBuilder.Entity<Reminder>().HasIndex(r => new { r.AccountGuid, r.IsDismissed });

        // Tenant isolation — global query filters. Account is intentionally NOT filtered
        // (login/register look up by NickName before any AccountGuid exists).
        modelBuilder.Entity<Goal>().HasQueryFilter(g => g.AccountFk == AccountGuid);
        modelBuilder.Entity<Journal>().HasQueryFilter(j => j.AccountFk == AccountGuid);
        modelBuilder.Entity<GoalProgress>().HasQueryFilter(p => p.AccountFk == AccountGuid);
        modelBuilder.Entity<Todo>().HasQueryFilter(t => t.AccountFk == AccountGuid);
        modelBuilder.Entity<Reminder>().HasQueryFilter(r => r.AccountGuid == AccountGuid);
        modelBuilder.Entity<AnalyticsEvent>().HasQueryFilter(e => e.AccountGuid == AccountGuid);

        // Encryption at rest — Phase 1: unbounded TEXT columns only (no schema ALTER needed).
        var conv = new EncryptedStringConverter(_enc);
        modelBuilder.Entity<Goal>().Property(g => g.GoalText).HasConversion(conv);
        modelBuilder.Entity<Goal>().Property(g => g.MeasurableOutcome).HasConversion(conv);
        modelBuilder.Entity<Goal>().Property(g => g.Steps).HasConversion(conv);
        modelBuilder.Entity<Journal>().Property(j => j.Notes).HasConversion(conv);
        modelBuilder.Entity<GoalProgress>().Property(p => p.NextStepItems).HasConversion(conv);
        modelBuilder.Entity<Todo>().Property(t => t.Notes).HasConversion(conv);
    }
}
