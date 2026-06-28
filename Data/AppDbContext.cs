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
    public DbSet<DbProduct>       Products      => Set<DbProduct>();
    public DbSet<DbUserProduct>   UserProducts  => Set<DbUserProduct>();
    public DbSet<DbTicket>        Tickets       => Set<DbTicket>();
    public DbSet<DbVisit>         Visits        => Set<DbVisit>();
    public DbSet<DbVisitActivity> VisitActivities => Set<DbVisitActivity>();

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

            e.HasOne(c => c.Support)
             .WithMany()
             .HasForeignKey(c => c.SupportId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Specialist)
             .WithMany()
             .HasForeignKey(c => c.SpecialistId)
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

        // ── DbProduct ────────────────────────────────────────────────────────
        modelBuilder.Entity<DbProduct>(e =>
        {
            e.HasIndex(p => p.ErpNextId).IsUnique();
            e.HasIndex(p => p.ItemCode).IsUnique();
        });

        // ── DbUserProduct ────────────────────────────────────────────────────
        modelBuilder.Entity<DbUserProduct>(e =>
        {
            e.HasOne(up => up.Product)
             .WithMany()
             .HasForeignKey(up => up.ProductId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(up => up.ErpNextLineName).IsUnique();
            e.HasIndex(up => up.CustomerErpNextUserId);
        });

        // ── DbTicket ─────────────────────────────────────────────────────────
        modelBuilder.Entity<DbTicket>(e =>
        {
            e.HasOne(t => t.Conversation)
             .WithMany()
             .HasForeignKey(t => t.ConversationId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(t => t.Specialist)
             .WithMany()
             .HasForeignKey(t => t.SpecialistAppUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.Support)
             .WithMany()
             .HasForeignKey(t => t.SupportAppUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(t => t.ErpNextId).IsUnique();
        });

        // ── DbVisit ──────────────────────────────────────────────────────────
        modelBuilder.Entity<DbVisit>(e =>
        {
            e.HasOne(v => v.Ticket)
             .WithMany()
             .HasForeignKey(v => v.TicketId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(v => v.Product)
             .WithMany()
             .HasForeignKey(v => v.ProductId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(v => v.ErpNextId).IsUnique();
        });
    }
}

