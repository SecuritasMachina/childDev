using ChildDev.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChildDev.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalProgress> GoalProgresses => Set<GoalProgress>();
    public DbSet<Todo> Todos => Set<Todo>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Journal>()
            .HasIndex(j => new { j.AccountFk, j.UpdatedOn });
        modelBuilder.Entity<Goal>()
            .HasIndex(g => new { g.AccountFk, g.UpdatedOn });
        modelBuilder.Entity<GoalProgress>()
            .HasIndex(p => new { p.AccountFk, p.UpdatedOn });
        modelBuilder.Entity<Todo>()
            .HasIndex(t => new { t.AccountFk, t.UpdatedOn });
        modelBuilder.Entity<Account>()
            .HasIndex(a => a.NickName)
            .IsUnique();
    }
}
