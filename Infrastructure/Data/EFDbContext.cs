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
        public DbSet<Like> Likes { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Collection> Collections { get; set; }
        public DbSet<Gift> Gifts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserMemory>(entity =>
            {
                entity.HasOne(um => um.User)
                      .WithMany(u => u.Memories)
                      .HasForeignKey(um => um.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasOne(prt => prt.User)
                      .WithMany()
                      .HasForeignKey(prt => prt.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Like>(entity =>
            {
                entity.HasOne(l => l.User)
                      .WithMany()
                      .HasForeignKey(l => l.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(l => l.Memory)
                      .WithMany()
                      .HasForeignKey(l => l.MemoryId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(l => new { l.UserId, l.MemoryId }).IsUnique();
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                entity.HasOne(c => c.User)
                      .WithMany()
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.Memory)
                      .WithMany()
                      .HasForeignKey(c => c.MemoryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Collection>(entity =>
            {
                entity.HasOne(c => c.User)
                      .WithMany()
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserMemory>(entity =>
            {
                entity.HasMany(um => um.Collections)
                      .WithMany(c => c.Memories)
                      .UsingEntity<Dictionary<string, object>>(
                          "CollectionMemory",
                          j => j.HasOne<Collection>().WithMany().HasForeignKey("CollectionId"),
                          j => j.HasOne<UserMemory>().WithMany().HasForeignKey("MemoryId"),
                          j => j.HasKey("CollectionId", "MemoryId"));
            });

            modelBuilder.Entity<Gift>(entity =>
            {
                entity.HasOne(g => g.Sender)
                      .WithMany()
                      .HasForeignKey(g => g.SenderId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(g => g.Memory)
                      .WithMany()
                      .HasForeignKey(g => g.MemoryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}