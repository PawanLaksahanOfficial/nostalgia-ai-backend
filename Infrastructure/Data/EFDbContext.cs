using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data
{
    public class EFDbContext : DbContext
    {
        public EFDbContext(DbContextOptions<EFDbContext> options) : base(options) 
        { }

        public DbSet<Memory> Memories { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserMemory> UserMemories { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
        public DbSet<VideoRequest> VideoRequests { get; set; }
        public DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; set; }
        public DbSet<MemoryShareLink> MemoryShareLinks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserMemory>(entity =>
            {
                entity.HasOne(um => um.User)
                      .WithMany(u => u.Memories)
                      .HasForeignKey(um => um.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(um => um.Status);
                entity.Property(um => um.StockPhotoCredit).HasMaxLength(500);
            });

            modelBuilder.Entity<VideoRequest>(entity =>
            {
                entity.Property(r => r.IpHash).HasMaxLength(64);
                entity.HasIndex(r => r.CreatedAt);
                entity.HasIndex(r => new { r.IpHash, r.CreatedAt });
            });

            modelBuilder.Entity<MemoryShareLink>(entity =>
            {
                entity.Property(s => s.Token).HasMaxLength(64).IsRequired();
                entity.HasIndex(s => s.Token).IsUnique();
                entity.HasIndex(s => s.UserMemoryId);
                entity.HasOne(s => s.UserMemory)
                      .WithMany(m => m.ShareLinks)
                      .HasForeignKey(s => s.UserMemoryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasOne(prt => prt.User)
                      .WithMany()
                      .HasForeignKey(prt => prt.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<EmailVerificationToken>(entity =>
            {
                entity.Property(t => t.Token).HasMaxLength(64).IsRequired();
                entity.HasOne(t => t.User)
                      .WithMany()
                      .HasForeignKey(t => t.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                // Not unique: existing accounts may already share one. New sign-ups are checked in code.
                entity.Property(u => u.CanonicalEmail).HasMaxLength(320);
                entity.HasIndex(u => u.CanonicalEmail);
                entity.Property(u => u.SignupIpHash).HasMaxLength(64);
                entity.HasIndex(u => new { u.SignupIpHash, u.CreatedDate });
                entity.HasIndex(u => u.StripeCustomerId);
                entity.HasIndex(u => u.StripeSubscriptionId);
            });

            modelBuilder.Entity<ProcessedStripeEvent>(entity =>
            {
                entity.HasIndex(e => e.EventId).IsUnique();
            });
        }
    }
}