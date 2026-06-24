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
        }
    }
}