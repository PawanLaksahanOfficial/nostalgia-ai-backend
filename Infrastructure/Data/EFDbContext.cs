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
        public DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; set; }

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
            });

            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasOne(prt => prt.User)
                      .WithMany()
                      .HasForeignKey(prt => prt.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
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