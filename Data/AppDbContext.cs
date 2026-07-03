using Microsoft.EntityFrameworkCore;
using VmTips.Web.Models;

namespace VmTips.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettings>()
            .HasKey(x => x.Id);
        modelBuilder.Entity<Participant>()
            .HasIndex(x => x.AccessCode)
            .IsUnique();

        modelBuilder.Entity<Team>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<Prediction>()
            .HasIndex(x => new { x.ParticipantId, x.MatchId })
            .IsUnique();

        modelBuilder.Entity<Match>()
            .HasOne(x => x.HomeTeam)
            .WithMany()
            .HasForeignKey(x => x.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasOne(x => x.AwayTeam)
            .WithMany()
            .HasForeignKey(x => x.AwayTeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
