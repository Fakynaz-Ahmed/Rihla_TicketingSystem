using Microsoft.EntityFrameworkCore;
using Rihla.Models.Db;

namespace Rihla.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser>         Users         => Set<AppUser>();
    public DbSet<AppToken>        Tokens        => Set<AppToken>();
    public DbSet<DbConversation>  Conversations => Set<DbConversation>();
    public DbSet<DeviceToken>     DeviceTokens  => Set<DeviceToken>();
    public DbSet<DbPassportData>  PassportData  => Set<DbPassportData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── AppUser ───────────────────────────────────────────────────────────
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.ErpNextUserId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Department);
        });

        // ── AppToken ──────────────────────────────────────────────────────────
        modelBuilder.Entity<AppToken>(e =>
        {
            e.HasIndex(t => t.Jti).IsUnique();
            e.HasIndex(t => t.ExpiresAt);
        });

        // ── DbConversation ────────────────────────────────────────────────────
        modelBuilder.Entity<DbConversation>(e =>
        {
            e.HasOne(c => c.User)
             .WithMany(u => u.Conversations)
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(c => c.UserId);
            e.HasIndex(c => c.Status);
            e.HasIndex(c => c.CreatedAt);
            e.HasIndex(c => new { c.UserId, c.CreatedAt });
        });

        // ── DeviceToken ───────────────────────────────────────────────────────
        modelBuilder.Entity<DeviceToken>(e =>
        {
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(t => new { t.UserId, t.Token }).IsUnique();
            e.HasIndex(t => t.UserId);
        });

        // ── DbPassportData ───────────────────────────────────────────────────
        modelBuilder.Entity<DbPassportData>(e =>
        {
            e.HasKey(p => p.PassportNumber);

            e.HasOne(p => p.UploadedByUser)
             .WithMany()
             .HasForeignKey(p => p.UploadedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Conversation)
             .WithMany()
             .HasForeignKey(p => p.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(p => p.UploadedByUserId);
            e.HasIndex(p => p.ConversationId);

            e.Property(p => p.Status)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.Property(p => p.MilitaryStatus)
             .HasConversion<string>()
             .HasMaxLength(50);
        });
    }
}
